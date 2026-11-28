import 'package:drift/drift.dart';
import 'package:talker/talker.dart';

import '../database/app_database.dart';
import 'protocol/sync_response.dart';
import 'protocol/sync_server_settings.dart';
import 'sync_data_type.dart';
import 'sync_local_data.dart';
import 'sync_notice.dart';

const _attemptsBeforeParking = 5;
const _failingPeriodBeforeParking = Duration(hours: 1);
const _noticeLifetime = Duration(days: 30);

typedef SyncResponseApplication = ({
  bool runAgainImmediately,
  List<String> problems,
});

/// Applies a readable response. Runs inside one transaction, the problems
/// are reported by the caller after the commit.
class SyncResponseApplier {
  SyncResponseApplier({required this.local, required this.talker});

  final SyncLocalData local;
  final Talker talker;

  AppDatabase get _database => local.database;

  Future<SyncResponseApplication> apply(
    SyncResponse response, {
    required SyncServerSettings settings,
    required List<SyncCommand> sentCommands,
    required SyncState sentState,
    required DateTime arrival,
  }) async {
    if (!await local.isUnchangedSince(sentState)) {
      return (runAgainImmediately: false, problems: const <String>[]);
    }
    final problems = <String>[];
    if (response.coversAllScopes) {
      await _removeScopesMissingFrom(response);
    }
    await _removeScopes(response, arrival);
    await _applyBlocks(response, problems);
    final (:removedCommand, :parkedCommand) = await _applyResults(
      response.results,
      sentCommands,
      arrival,
      problems,
    );
    await local.reapplyQueuedCommands();
    await local.updateState(
      SyncStatesCompanion(
        token: Value(parkedCommand ? null : response.token),
        clockOffsetMilliseconds: Value(
          response.serverTime - arrival.millisecondsSinceEpoch,
        ),
        pollIntervalSeconds: Value.absentIfNull(settings.pollIntervalSeconds),
        maxCommandsPerRequest: Value.absentIfNull(
          settings.maxCommandsPerRequest,
        ),
        maxRequestBytes: Value.absentIfNull(settings.maxRequestBytes),
        firstSyncCompleted: const Value(true),
      ),
    );
    await (_database.delete(_database.syncNotices)..where(
          (notice) => notice.createdAt.isSmallerThanValue(
            arrival.subtract(_noticeLifetime).millisecondsSinceEpoch,
          ),
        ))
        .go();
    return (
      runAgainImmediately:
          response.hasMore ||
          (removedCommand && await local.commandCount() > 0),
      problems: problems,
    );
  }

  Future<void> _removeScopesMissingFrom(SyncResponse response) async {
    final responseScopeIds = {
      for (final block in response.scopes) block.scopeId,
    };
    for (final scope in await _database.select(_database.syncScopes).get()) {
      if (!responseScopeIds.contains(scope.scopeId)) {
        await local.removeScope(scope.scopeId);
      }
    }
  }

  Future<void> _removeScopes(SyncResponse response, DateTime arrival) async {
    for (final removedScope in response.removedScopes) {
      final scopeId = removedScope.scopeId;
      final scope = await (_database.select(
        _database.syncScopes,
      )..where((scope) => scope.scopeId.equals(scopeId))).getSingleOrNull();
      final label = switch (scope) {
        final scope? =>
          await local
              .rootDataTypeOf(scope.kind)
              ?.scopeLabel(_database, scopeId),
        null => null,
      };
      await local.removeScope(scopeId);
      await (_database.delete(
        _database.syncCommands,
      )..where((command) => command.scopeId.equals(scopeId))).go();
      if (!removedScope.removedByOwnAccount) {
        await local.addNotice(
          SyncNoticeKind.removedScope,
          scopeId: scopeId,
          createdAt: arrival,
          label: label,
        );
      }
    }
  }

  Future<void> _applyBlocks(
    SyncResponse response,
    List<String> problems,
  ) async {
    for (final block in response.scopes) {
      final scopeId = block.scopeId;
      await _database
          .into(_database.syncScopes)
          .insertOnConflictUpdate(
            SyncScopesCompanion.insert(scopeId: scopeId, kind: block.kind),
          );
      if (block.fullState) {
        for (final dataType in local.dataTypes) {
          await dataType.deleteScopeRecords(_database, scopeId);
        }
      }
      for (final record in block.records) {
        final dataType = local.dataTypeNamed(record.dataType);
        if (dataType == null) {
          continue;
        }
        if (record.fields case final Map<String, Object?> fields
            when await _writeServerRecord(
              dataType,
              scopeId: scopeId,
              recordId: record.id,
              fields: fields,
            )) {
          continue;
        }
        talker.warning(
          'Skipped the sync record ${record.id} of ${record.dataType}',
        );
        problems.add('Skipped a sync record of ${record.dataType}');
      }
      for (final deletedRecord in block.deletedRecords) {
        await local
            .dataTypeNamed(deletedRecord.dataType)
            ?.deleteRecord(
              _database,
              scopeId: scopeId,
              recordId: deletedRecord.id,
            );
      }
      await (_database.delete(
        _database.syncMembers,
      )..where((member) => member.scopeId.equals(scopeId))).go();
      for (final member in block.members) {
        await _database
            .into(_database.syncMembers)
            .insert(
              SyncMembersCompanion.insert(
                scopeId: scopeId,
                memberId: member.memberId,
                name: member.name,
                role: member.role,
                state: member.state.name,
                isOwn: member.isOwn,
              ),
            );
      }
    }
  }

  /// Returns `false` for a record the data type failed to write, whose
  /// partial changes are rolled back by the savepoint of the nested
  /// transaction.
  Future<bool> _writeServerRecord(
    SyncDataType dataType, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) async {
    try {
      await _database.transaction(
        () => dataType.writeServerRecord(
          _database,
          scopeId: scopeId,
          recordId: recordId,
          fields: fields,
        ),
      );
      return true;
    } on Exception {
      return false;
    }
  }

  Future<({bool removedCommand, bool parkedCommand})> _applyResults(
    List<SyncCommandResult> results,
    List<SyncCommand> sentCommands,
    DateTime arrival,
    List<String> problems,
  ) async {
    var removedCommand = false;
    var parkedCommand = false;
    for (final (index, result) in results.indexed) {
      final command = sentCommands[index];
      final original = result.original;
      final (
        status,
        reason,
      ) = result.status == SyncResultStatus.alreadyProcessed
          ? (original!.status, original.reason)
          : (result.status, result.reason);
      switch (status) {
        case SyncResultStatus.applied || SyncResultStatus.ignored:
          removedCommand |= await local.deleteCommand(command.id);
        case SyncResultStatus.rejected:
          if (await local.deleteCommand(command.id)) {
            removedCommand = true;
            await local.addNotice(
              SyncNoticeKind.rejectedCommand,
              scopeId: command.scopeId,
              createdAt: arrival,
              dataType: command.dataType,
              recordId: command.recordId,
              commandKind: command.kind,
              reason: (reason ?? SyncRejectionReason.invalid).name,
            );
          }
        case SyncResultStatus.failed:
          if (await _countFailedAttempt(command.id, arrival)) {
            parkedCommand = true;
            problems.add(parkedCommandProblem(command, 'failed repeatedly'));
          }
        case SyncResultStatus.accepted || SyncResultStatus.alreadyProcessed:
          break;
      }
    }
    return (removedCommand: removedCommand, parkedCommand: parkedCommand);
  }

  /// Returns whether the command was parked.
  Future<bool> _countFailedAttempt(String commandId, DateTime arrival) async {
    final command = await local.commandWithId(commandId);
    if (command == null) {
      return false;
    }
    final failedAttempts = command.failedAttempts + 1;
    final firstFailedAt =
        command.firstFailedAt ?? arrival.millisecondsSinceEpoch;
    if (failedAttempts >= _attemptsBeforeParking &&
        arrival.millisecondsSinceEpoch - firstFailedAt >=
            _failingPeriodBeforeParking.inMilliseconds) {
      return local.park(command, at: arrival);
    }
    await (_database.update(
      _database.syncCommands,
    )..where((row) => row.id.equals(commandId))).write(
      SyncCommandsCompanion(
        failedAttempts: Value(failedAttempts),
        firstFailedAt: Value(firstFailedAt),
      ),
    );
    return false;
  }
}
