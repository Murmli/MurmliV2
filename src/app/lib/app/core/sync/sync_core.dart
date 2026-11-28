import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:clock/clock.dart';
import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:talker/talker.dart';

import '../api/api_client_provider.dart';
import '../authentication/signed_in_account_provider.dart';
import '../crash_reporting/crash_reporter.dart';
import '../crash_reporting/crash_reporter_provider.dart';
import '../database/app_database.dart';
import '../database/app_database_provider.dart';
import '../identifiers/new_uuid.dart';
import '../logging/talker_provider.dart';
import '../time/clock_provider.dart';
import 'protocol/sync_request.dart';
import 'protocol/sync_server_settings.dart';
import 'sync_command_kind.dart';
import 'sync_data_type.dart';
import 'sync_data_types_provider.dart';
import 'sync_local_data.dart';
import 'sync_notice.dart';
import 'sync_response_applier.dart';
import 'sync_response_reader.dart';
import 'sync_round_outcome.dart';
import 'sync_write_transaction.dart';

part 'sync_core.g.dart';

const _builtInMaxRequestBytes = 1000000;
const _builtInMaxCommandsPerRequest = 500;
const _clockJumpTolerance = Duration(minutes: 5);
const _applyFailuresBeforeDiscardingToken = 3;
const _problemTypePrefix = 'tag:murmli.de,2026:';
const _requestTimeout = Duration(seconds: 60);

@Riverpod(keepAlive: true)
SyncCore syncCore(Ref ref) {
  final core = SyncCore(
    database: ref.watch(appDatabaseProvider),
    apiClient: ref.watch(apiClientProvider),
    clock: ref.watch(appClockProvider),
    dataTypes: ref.watch(syncDataTypesProvider),
    crashReporter: ref.watch(crashReporterProvider),
    talker: ref.watch(talkerProvider),
  );
  ref
    ..onDispose(core.dispose)
    ..listen(
      signedInAccountProvider,
      (previous, next) => unawaited(core.adoptAccount(next?.id)),
      fireImmediately: true,
    );
  return core;
}

class SyncNotReadyException implements Exception {
  const SyncNotReadyException();

  @override
  String toString() =>
      'SyncNotReadyException: no account or no completed first sync yet';
}

typedef _RequestInputs = ({
  SyncState state,
  List<SyncCommand> commands,
  int queueLength,
});

typedef _PlannedRequest = ({
  SyncState state,
  List<SyncCommand> sentCommands,
  SyncRequest body,
});

class SyncCore {
  SyncCore({
    required AppDatabase database,
    required this._apiClient,
    required this._clock,
    required List<SyncDataType> dataTypes,
    required this._crashReporter,
    required this._talker,
  }) : _database = database,
       _local = SyncLocalData(database: database, dataTypes: dataTypes);

  final AppDatabase _database;
  final Dio _apiClient;
  final Clock _clock;
  final CrashReporter _crashReporter;
  final Talker _talker;
  final SyncLocalData _local;
  late final _applier = SyncResponseApplier(local: _local, talker: _talker);
  final _commandsQueued = StreamController<void>.broadcast();

  Future<void> _accountOperations = Future.value();
  String? _expectedAccountId;
  Future<SyncRoundOutcome>? _runningRound;
  Future<SyncRoundOutcome>? _followUpRound;
  int? _commandLimitAfterTooLarge;
  var _consecutiveApplyFailures = 0;
  CancelToken? _requestCancelToken;

  Stream<void> get commandsQueued => _commandsQueued.stream;

  /// Runs [action] in one transaction: either all its changes and commands
  /// are stored or none. [action] must not await work outside the database,
  /// which would block all database access meanwhile. Throws
  /// [SyncNotReadyException] before the first completed sync of the
  /// signed-in account.
  Future<T> write<T>(
    Future<T> Function(SyncWriteTransaction transaction) action,
  ) async {
    final (result, queuedCommand) = await _database.transaction(() async {
      final state = await _local.readState();
      if (!_isReady(state)) {
        throw const SyncNotReadyException();
      }
      final transaction = _SyncWriteTransaction(
        local: _local,
        clock: _clock,
        state: state,
      );
      final result = await action(transaction);
      if (transaction.lastCommandTime != state.lastCommandTime) {
        await _local.updateState(
          SyncStatesCompanion(
            lastCommandTime: Value(transaction.lastCommandTime),
          ),
        );
      }
      return (result, transaction.queuedCommand);
    });
    if (queuedCommand && !_commandsQueued.isClosed) {
      _commandsQueued.add(null);
    }
    return result;
  }

  /// Binds the local data to the signed-in account. Data of another account
  /// is wiped first, `null` wipes everything including the device id. Does
  /// not wait for a running round: its response no longer matches the token
  /// and generation and is dropped. `null` also cancels a running request.
  /// Until the stored account is [accountId], for example after a failed
  /// wipe, rounds and writes do nothing.
  Future<void> adoptAccount(String? accountId) {
    if (accountId == null) {
      _requestCancelToken?.cancel();
    }
    _expectedAccountId = accountId;
    final result = _accountOperations.then((_) async {
      final wiped = await _database.transaction(() async {
        if (accountId == null) {
          await _local.wipe();
          return true;
        }
        return _local.adoptAccount(accountId);
      });
      if (wiped) {
        _resetInMemoryLimits();
      }
    });
    _accountOperations = result.then<void>((_) {}, onError: (Object _) {});
    return result;
  }

  /// Whether the first sync of the signed-in account has completed, so its
  /// data can be shown and [write] accepts changes.
  Stream<bool> watchFirstSyncCompleted() =>
      _local.watchState().map(_isReady).distinct();

  Stream<bool> watchHasPendingCommands(String recordId) =>
      (_database.select(_database.syncCommands)
            ..where((command) => command.recordId.equals(recordId))
            ..limit(1))
          .watch()
          .map((commands) => commands.isNotEmpty)
          .distinct();

  Future<int> pendingCommandCount() => _local.commandCount();

  /// The poll interval the server last sent, `null` before its first
  /// readable response.
  Future<Duration?> pollInterval() async =>
      switch ((await _local.readState()).pollIntervalSeconds) {
        final seconds? => Duration(seconds: seconds),
        null => null,
      };

  Stream<List<SyncNotice>> watchNotices() =>
      (_database.select(_database.syncNotices)
            ..orderBy([(notice) => OrderingTerm.asc(notice.id)]))
          .watch()
          .map((notices) => [for (final notice in notices) _toNotice(notice)]);

  Future<void> dismissNotice(int id) => (_database.delete(
    _database.syncNotices,
  )..where((notice) => notice.id.equals(id))).go();

  Stream<List<SyncMember>> watchMembers(String scopeId) =>
      (_database.select(_database.syncMembers)
            ..where((member) => member.scopeId.equals(scopeId))
            ..orderBy([(member) => OrderingTerm.asc(member.memberId)]))
          .watch();

  /// Makes the next round fetch the full state, for example after an app
  /// update that knows new data types or fields.
  Future<void> discardToken() => _database.transaction(_local.discardToken);

  /// Runs one round. Calls during a running round share exactly one
  /// follow-up round.
  Future<SyncRoundOutcome> runRound() {
    final runningRound = _runningRound;
    if (runningRound == null) {
      final round = _runRound().whenComplete(() {
        _runningRound = null;
      });
      _runningRound = round;
      return round;
    }
    return _followUpRound ??= runningRound
        .then<void>((_) {}, onError: (Object _) {})
        .then((_) {
          _followUpRound = null;
          return runRound();
        });
  }

  Future<void> dispose() => _commandsQueued.close();

  Future<SyncRoundOutcome> _runRound() async {
    await _accountOperations;
    try {
      return await _runRoundSteps();
    } on Object catch (error, stackTrace) {
      _talker.handle(error, stackTrace, 'A sync round failed');
      if (error is! Exception) {
        await _crashReporter.reportProblem('A sync round failed unexpectedly');
      }
      return const SyncRoundOutcome.failed();
    }
  }

  Future<SyncRoundOutcome> _runRoundSteps() async {
    while (true) {
      final inputs = await _database.transaction(_readRequestInputs);
      if (!_isExpectedAccount(inputs.state) || inputs.state.deviceId == null) {
        return const SyncRoundOutcome.notSignedIn();
      }
      final request = _planRequest(inputs);
      if (request.sentCommands.isNotEmpty || inputs.commands.isEmpty) {
        return _send(request);
      }
      await _parkOversizedCommand(inputs.commands.first);
    }
  }

  Future<SyncRoundOutcome> _send(_PlannedRequest request) async {
    final sentCommands = request.sentCommands;
    final cancelToken = CancelToken();
    _requestCancelToken = cancelToken;
    final timeout = Timer(_requestTimeout, cancelToken.cancel);
    final Response<String> response;
    try {
      response = await _apiClient.post<String>(
        '/v1/sync',
        data: request.body.toMap(),
        cancelToken: cancelToken,
        options: Options(
          headers: {'Murmli-Device-Id': request.state.deviceId},
          contentType: Headers.jsonContentType,
          responseType: ResponseType.plain,
          // A 401 must reach the interceptors as an error so the access
          // token can be renewed.
          validateStatus: (status) => status != 401,
          extra: {unreportedFailureKey: true},
        ),
      );
    } on DioException {
      return const SyncRoundOutcome.failed();
    } finally {
      timeout.cancel();
      if (identical(_requestCancelToken, cancelToken)) {
        _requestCancelToken = null;
      }
    }
    final arrival = _clock.now();
    final body = response.data ?? '';
    if (response.statusCode != 200) {
      return _handleProblem(response, body, request);
    }
    final readResponse = readSyncResponse(body, [
      for (final command in sentCommands) command.id,
    ]);
    if (readResponse == null) {
      return const SyncRoundOutcome.failed();
    }
    _commandLimitAfterTooLarge = null;
    final SyncResponseApplication application;
    try {
      application = await _database.transaction(
        () => _applier.apply(
          readResponse.response,
          settings: readResponse.settings,
          sentCommands: sentCommands,
          sentState: request.state,
          arrival: arrival,
        ),
      );
    } on Object catch (exception, stackTrace) {
      _talker.handle(exception, stackTrace, 'Applying a sync response failed');
      if (++_consecutiveApplyFailures >= _applyFailuresBeforeDiscardingToken) {
        _consecutiveApplyFailures = 0;
        await discardToken();
        await _crashReporter.reportProblem(
          'Applying a sync response failed repeatedly',
        );
      }
      return const SyncRoundOutcome.failed();
    }
    _consecutiveApplyFailures = 0;
    for (final problem in application.problems) {
      await _crashReporter.reportProblem(problem);
    }
    return SyncRoundOutcome.completed(
      runAgainImmediately: application.runAgainImmediately,
    );
  }

  Future<_RequestInputs> _readRequestInputs() async {
    final state = await _local.readState();
    final maxCommandsPerRequest =
        state.maxCommandsPerRequest ?? _builtInMaxCommandsPerRequest;
    final commandLimit = min(
      maxCommandsPerRequest,
      _commandLimitAfterTooLarge ?? maxCommandsPerRequest,
    );
    final commands =
        await (_database.select(_database.syncCommands)
              ..orderBy([(command) => OrderingTerm.asc(command.sequence)])
              ..limit(commandLimit))
            .get();
    return (
      state: state,
      commands: commands,
      queueLength: await _local.commandCount(),
    );
  }

  /// Takes commands in order while the body stays within the size limit, so
  /// no command is sent when the first one does not fit on its own.
  _PlannedRequest _planRequest(_RequestInputs inputs) {
    final state = inputs.state;
    final commands = inputs.commands;
    final correctedNow =
        _clock.now().millisecondsSinceEpoch + state.clockOffsetMilliseconds;
    SyncRequest requestWith(List<SyncRequestCommand> wireCommands) =>
        SyncRequest(
          token: state.token,
          dataTypes: [for (final dataType in _local.dataTypes) dataType.name],
          commands: wireCommands,
          pendingCommandCount: inputs.queueLength,
          oldestPendingCommandAgeSeconds: commands.isEmpty
              ? null
              : max(0, (correctedNow - commands.first.time) ~/ 1000),
        );
    final maxRequestBytes = state.maxRequestBytes ?? _builtInMaxRequestBytes;
    var requestBytes = _encodedLength(requestWith(const []).toMap());
    final wireCommands = <SyncRequestCommand>[];
    for (final command in commands) {
      final wireCommand = _toWireCommand(command);
      final addedBytes =
          _encodedLength(wireCommand.toMap()) + (wireCommands.isEmpty ? 0 : 1);
      if (requestBytes + addedBytes > maxRequestBytes) {
        break;
      }
      requestBytes += addedBytes;
      wireCommands.add(wireCommand);
    }
    return (
      state: state,
      sentCommands: commands.sublist(0, wireCommands.length),
      body: requestWith(wireCommands),
    );
  }

  Future<SyncRoundOutcome> _handleProblem(
    Response<String> response,
    String body,
    _PlannedRequest request,
  ) async {
    final readProblem = readProblemDetails(body);
    if (readProblem == null) {
      return const SyncRoundOutcome.failed();
    }
    switch ((response.statusCode, readProblem.problem.type)) {
      case (410, '${_problemTypePrefix}account-gone'):
        final wiped = await _database.transaction(() async {
          if (!await _local.isUnchangedSince(request.state)) {
            return false;
          }
          await _local.wipe();
          return true;
        });
        if (!wiped) {
          return const SyncRoundOutcome.completed(runAgainImmediately: false);
        }
        _resetInMemoryLimits();
        return const SyncRoundOutcome.accountGone();
      case (403, '${_problemTypePrefix}app-version-unsupported'):
        return const SyncRoundOutcome.appVersionUnsupported();
      case (413, '${_problemTypePrefix}request-too-large')
          when request.sentCommands.isNotEmpty:
        return SyncRoundOutcome.completed(
          runAgainImmediately: await _handleTooLarge(
            readProblem.settings,
            request,
          ),
        );
      case (429, '${_problemTypePrefix}rate-limited') ||
          (503, '${_problemTypePrefix}service-unavailable'):
        final retryAfterSeconds = int.tryParse(
          response.headers.value('retry-after')?.trim() ?? '',
        );
        if (retryAfterSeconds != null && retryAfterSeconds >= 1) {
          return SyncRoundOutcome.retryAfter(
            Duration(seconds: retryAfterSeconds),
          );
        }
    }
    return const SyncRoundOutcome.failed();
  }

  /// Returns `false` for a response that no longer belongs to the local data.
  Future<bool> _handleTooLarge(
    SyncServerSettings settings,
    _PlannedRequest request,
  ) async {
    final sentCommands = request.sentCommands;
    final (:current, :parkedCommand) = await _database.transaction(() async {
      if (!await _local.isUnchangedSince(request.state)) {
        return (current: false, parkedCommand: false);
      }
      await _local.updateState(
        SyncStatesCompanion(
          maxRequestBytes: Value.absentIfNull(settings.maxRequestBytes),
          maxCommandsPerRequest: Value.absentIfNull(
            settings.maxCommandsPerRequest,
          ),
        ),
      );
      return (
        current: true,
        parkedCommand:
            sentCommands.length == 1 &&
            await _local.park(sentCommands.single, at: _clock.now()),
      );
    });
    if (!current) {
      return false;
    }
    if (parkedCommand) {
      await _crashReporter.reportProblem(
        parkedCommandProblem(sentCommands.single, 'too large'),
      );
    } else if (sentCommands.length > 1) {
      _commandLimitAfterTooLarge = sentCommands.length ~/ 2;
    }
    return true;
  }

  Future<void> _parkOversizedCommand(SyncCommand command) async {
    if (await _database.transaction(
      () => _local.park(command, at: _clock.now()),
    )) {
      await _crashReporter.reportProblem(
        parkedCommandProblem(command, 'too large'),
      );
    }
  }

  bool _isReady(SyncState state) =>
      _isExpectedAccount(state) && state.firstSyncCompleted;

  bool _isExpectedAccount(SyncState state) =>
      state.accountId != null && state.accountId == _expectedAccountId;

  void _resetInMemoryLimits() {
    _commandLimitAfterTooLarge = null;
    _consecutiveApplyFailures = 0;
  }

  SyncNotice _toNotice(SyncNoticeRow notice) => SyncNotice(
    id: notice.id,
    kind: SyncNoticeKind.values.byName(notice.kind),
    scopeId: notice.scopeId,
    dataType: notice.dataType,
    recordId: notice.recordId,
    commandKind: SyncCommandKind.values.asNameMap()[notice.commandKind],
    reason: notice.reason,
    label: notice.label,
    createdAt: DateTime.fromMillisecondsSinceEpoch(notice.createdAt),
  );
}

class _SyncWriteTransaction implements SyncWriteTransaction {
  _SyncWriteTransaction({
    required this._local,
    required this._clock,
    required SyncState state,
  }) : _clockOffsetMilliseconds = state.clockOffsetMilliseconds,
       lastCommandTime = state.lastCommandTime;

  final SyncLocalData _local;
  final Clock _clock;
  final int _clockOffsetMilliseconds;
  int? lastCommandTime;
  var queuedCommand = false;

  @override
  Future<void> create({
    required SyncDataType dataType,
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) {
    dataType.checkLimits(SyncCommandKind.create, fields);
    return _queue(
      SyncCommandKind.create,
      dataType,
      scopeId: scopeId,
      recordId: recordId,
      fields: fields,
    );
  }

  @override
  Future<void> setFields({
    required SyncDataType dataType,
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) {
    dataType.checkLimits(SyncCommandKind.setFields, fields);
    return _queue(
      SyncCommandKind.setFields,
      dataType,
      scopeId: scopeId,
      recordId: recordId,
      fields: fields,
    );
  }

  @override
  Future<void> delete({
    required SyncDataType dataType,
    required String scopeId,
    required String recordId,
  }) => _queue(
    SyncCommandKind.delete,
    dataType,
    scopeId: scopeId,
    recordId: recordId,
    fields: null,
  );

  Future<void> _queue(
    SyncCommandKind kind,
    SyncDataType dataType, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?>? fields,
  }) async {
    if (_local.dataTypeNamed(dataType.name) != dataType) {
      throw ArgumentError.value(dataType.name, 'dataType', 'not registered');
    }
    await _local.applyCommand(
      kind,
      dataType,
      scopeId: scopeId,
      recordId: recordId,
      fields: fields,
    );
    final database = _local.database;
    await database
        .into(database.syncCommands)
        .insert(
          SyncCommandsCompanion.insert(
            id: newUuid(),
            kind: kind.name,
            scopeId: scopeId,
            dataType: dataType.name,
            recordId: recordId,
            fields: Value(fields == null ? null : jsonEncode(fields)),
            time: _nextCommandTime(),
          ),
        );
    queuedCommand = true;
  }

  // A small backward jump of the clock keeps the time of the previous
  // command, so an action never loses against the one before it.
  int _nextCommandTime() {
    final now = _clock.now().millisecondsSinceEpoch + _clockOffsetMilliseconds;
    final previous = lastCommandTime;
    final time =
        previous != null &&
            now < previous &&
            previous - now <= _clockJumpTolerance.inMilliseconds
        ? previous
        : now;
    lastCommandTime = time;
    return time;
  }
}

int _encodedLength(Object? json) => utf8.encode(jsonEncode(json)).length;

SyncRequestCommand _toWireCommand(SyncCommand command) => SyncRequestCommand(
  id: command.id,
  kind: command.kind,
  scopeId: command.scopeId,
  dataType: command.dataType,
  recordId: command.recordId,
  time: command.time,
  fields: decodeCommandFields(command),
);
