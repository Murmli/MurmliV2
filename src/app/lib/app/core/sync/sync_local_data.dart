import 'dart:convert';

import 'package:drift/drift.dart';

import '../database/app_database.dart';
import '../identifiers/new_uuid.dart';
import 'sync_command_kind.dart';
import 'sync_data_type.dart';
import 'sync_notice.dart';

const _stateId = 1;
const _initialState = SyncState(
  id: _stateId,
  clockOffsetMilliseconds: 0,
  localStateGeneration: 0,
  firstSyncCompleted: false,
);

/// The local operations of the sync core. Callers run them inside a
/// transaction of [database].
class SyncLocalData {
  SyncLocalData({required this.database, required this.dataTypes})
    : _dataTypesByName = {
        for (final dataType in dataTypes) dataType.name: dataType,
      };

  final AppDatabase database;
  final List<SyncDataType> dataTypes;
  final Map<String, SyncDataType> _dataTypesByName;

  SyncDataType? dataTypeNamed(String name) => _dataTypesByName[name];

  SyncDataType? rootDataTypeOf(String scopeKind) => dataTypes
      .where((dataType) => dataType.rootScopeKind == scopeKind)
      .firstOrNull;

  Future<SyncState> readState() async =>
      await _selectState().getSingleOrNull() ?? _initialState;

  Stream<SyncState> watchState() =>
      _selectState().watchSingleOrNull().map((state) => state ?? _initialState);

  /// Whether a response to a request that read [sentState] still belongs to
  /// the local data: neither the token nor the generation changed since.
  Future<bool> isUnchangedSince(SyncState sentState) async {
    final state = await readState();
    return state.token == sentState.token &&
        state.localStateGeneration == sentState.localStateGeneration;
  }

  Future<void> updateState(SyncStatesCompanion changes) async {
    await database
        .into(database.syncStates)
        .insert(
          const SyncStatesCompanion(id: Value(_stateId)),
          mode: InsertMode.insertOrIgnore,
        );
    await (database.update(
      database.syncStates,
    )..where((state) => state.id.equals(_stateId))).write(changes);
  }

  Future<int> commandCount() async {
    final count = database.syncCommands.id.count();
    final row = await (database.selectOnly(
      database.syncCommands,
    )..addColumns([count])).getSingle();
    return row.read(count) ?? 0;
  }

  Future<SyncCommand?> commandWithId(String commandId) => (database.select(
    database.syncCommands,
  )..where((command) => command.id.equals(commandId))).getSingleOrNull();

  Future<bool> deleteCommand(String commandId) async {
    final deletedRows = await (database.delete(
      database.syncCommands,
    )..where((command) => command.id.equals(commandId))).go();
    return deletedRows > 0;
  }

  Future<void> applyCommand(
    SyncCommandKind kind,
    SyncDataType dataType, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?>? fields,
    required int time,
  }) async {
    switch (kind) {
      case SyncCommandKind.create:
        await dataType.createRecordIfMissing(
          database,
          scopeId: scopeId,
          recordId: recordId,
          fields: fields ?? const {},
          time: time,
        );
        if (dataType.rootScopeKind case final scopeKind?) {
          await database
              .into(database.syncScopes)
              .insert(
                SyncScopesCompanion.insert(scopeId: scopeId, kind: scopeKind),
                mode: InsertMode.insertOrIgnore,
              );
        }
      case SyncCommandKind.setFields:
        await dataType.updateRecordIfPresent(
          database,
          scopeId: scopeId,
          recordId: recordId,
          fields: fields ?? const {},
          time: time,
        );
      case SyncCommandKind.delete:
        if (dataType.rootScopeKind == null) {
          await dataType.deleteRecord(
            database,
            scopeId: scopeId,
            recordId: recordId,
          );
        } else {
          await removeScope(scopeId);
        }
    }
  }

  Future<void> reapplyQueuedCommands() async {
    final commands = await (database.select(
      database.syncCommands,
    )..orderBy([(command) => OrderingTerm.asc(command.sequence)])).get();
    for (final command in commands) {
      final dataType = dataTypeNamed(command.dataType);
      final kind = SyncCommandKind.values.asNameMap()[command.kind];
      if (dataType == null || kind == null) {
        continue;
      }
      await applyCommand(
        kind,
        dataType,
        scopeId: command.scopeId,
        recordId: command.recordId,
        fields: decodeCommandFields(command),
        time: command.time,
      );
    }
  }

  /// Removes the records, members and the scope row. Queued commands stay.
  Future<void> removeScope(String scopeId) async {
    for (final dataType in dataTypes) {
      await dataType.deleteScopeRecords(database, scopeId);
    }
    await (database.delete(
      database.syncMembers,
    )..where((member) => member.scopeId.equals(scopeId))).go();
    await (database.delete(
      database.syncScopes,
    )..where((scope) => scope.scopeId.equals(scopeId))).go();
  }

  Future<void> addNotice(
    SyncNoticeKind kind, {
    required String scopeId,
    required DateTime createdAt,
    String? dataType,
    String? recordId,
    String? commandKind,
    String? reason,
    String? label,
  }) => database
      .into(database.syncNotices)
      .insert(
        SyncNoticesCompanion.insert(
          kind: kind.name,
          scopeId: scopeId,
          dataType: Value(dataType),
          recordId: Value(recordId),
          commandKind: Value(commandKind),
          reason: Value(reason),
          label: Value(label),
          createdAt: createdAt.millisecondsSinceEpoch,
        ),
      );

  Future<void> discardToken() async {
    final state = await readState();
    await updateState(
      SyncStatesCompanion(
        token: const Value(null),
        localStateGeneration: Value(state.localStateGeneration + 1),
      ),
    );
  }

  /// Takes the command out of the queue without a decision of the server. The
  /// discarded token makes the next round fetch the full state, which undoes
  /// the local effect of the command even if the app ends right after.
  /// Returns `false` if the command has already left the queue.
  Future<bool> park(SyncCommand command, {required DateTime at}) async {
    if (!await deleteCommand(command.id)) {
      return false;
    }
    await addNotice(
      SyncNoticeKind.parkedCommand,
      scopeId: command.scopeId,
      createdAt: at,
      dataType: command.dataType,
      recordId: command.recordId,
      commandKind: command.kind,
    );
    await discardToken();
    return true;
  }

  /// Returns whether the local data was wiped first.
  Future<bool> adoptAccount(String accountId) async {
    var state = await readState();
    final wipesOtherAccount =
        state.accountId != accountId &&
        (state.accountId != null || await _hasData(state));
    if (wipesOtherAccount) {
      await wipe();
      state = await readState();
    }
    await updateState(
      SyncStatesCompanion(
        accountId: Value(accountId),
        deviceId: Value(state.deviceId ?? newUuid()),
      ),
    );
    return wipesOtherAccount;
  }

  /// Empties every table of [database] and the tables of the data types.
  /// Only the generation survives, increased, so a response to a request
  /// before the wipe no longer applies.
  Future<void> wipe() async {
    final generation = (await readState()).localStateGeneration;
    for (final table in database.allTables) {
      await database.delete(table).go();
    }
    for (final dataType in dataTypes) {
      await dataType.deleteAllRecords(database);
    }
    await database
        .into(database.syncStates)
        .insert(
          SyncStatesCompanion.insert(
            id: const Value(_stateId),
            localStateGeneration: Value(generation + 1),
          ),
        );
  }

  SimpleSelectStatement<$SyncStatesTable, SyncState> _selectState() =>
      database.select(database.syncStates)
        ..where((state) => state.id.equals(_stateId));

  Future<bool> _hasData(SyncState state) async =>
      state.token != null ||
      await commandCount() > 0 ||
      await _hasAnyRow(database.syncScopes) ||
      await _hasAnyRow(database.syncNotices);

  Future<bool> _hasAnyRow<TableType extends HasResultSet, Row>(
    ResultSetImplementation<TableType, Row> table,
  ) async => (await (database.select(table)..limit(1)).get()).isNotEmpty;
}

Map<String, Object?>? decodeCommandFields(SyncCommand command) =>
    switch (command.fields) {
      final fields? => jsonDecode(fields) as Map<String, Object?>,
      null => null,
    };

String parkedCommandProblem(SyncCommand command, String cause) =>
    'Parked a sync command: ${command.dataType} ${command.kind} ($cause)';
