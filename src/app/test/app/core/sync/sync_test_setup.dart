import 'dart:async';
import 'dart:collection';
import 'dart:convert';
import 'dart:math';

import 'package:clock/clock.dart';
import 'package:dio/dio.dart';
import 'package:drift/drift.dart' show OrderingTerm;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/crash_reporting/crash_reporter_provider.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/lifecycle/app_foreground_provider.dart';
import 'package:app/app/core/network/network_available_provider.dart';
import 'package:app/app/core/sync/sync_core.dart';
import 'package:app/app/core/sync/sync_data_types_provider.dart';
import 'package:app/app/core/sync/sync_random_provider.dart';
import 'package:app/app/core/sync/sync_round_outcome.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/crash_reporter.dart';
import '../../../helpers/http_client_adapter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';
import 'test_data_types.dart';

const personalScopeId = 'personal-scope';
const firstToken = 'first-token';
const roundOutcome = SyncRoundOutcome.completed(runAgainImmediately: false);
const roundOutcomeRunningAgain = SyncRoundOutcome.completed(
  runAgainImmediately: true,
);

typedef RequestBody = Map<String, Object?>;
typedef ResponseJson = Map<String, Object?> Function(RequestBody requestBody);

/// A sync core signed in with [accountId] on an in-memory database, a
/// controllable clock, a scripted server and fixed randomness, in the
/// foreground with a network.
class SyncTestHarness {
  SyncTestHarness._(this.dataTypes);

  static Future<SyncTestHarness> start({
    List<TestDataType> dataTypes = testDataTypes,
    Clock? clock,
  }) async {
    final harness = SyncTestHarness._(dataTypes);
    final database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);
    await createTestTables(database, dataTypes);
    final container = createContainer(
      await createPreferences(),
      database: database,
      clock: clock ?? Clock(() => harness.now),
      overrides: [
        syncDataTypesProvider.overrideWithValue(dataTypes),
        appForegroundProvider.overrideWithBuild((ref, notifier) => true),
        networkAvailableProvider.overrideWithBuild((ref, notifier) => true),
        syncRandomProvider.overrideWithValue(harness.random),
      ],
    );
    addTearDown(container.dispose);
    await signIn(container);
    container.read(apiClientProvider).httpClientAdapter =
        harness.server.adapter;
    harness
      ..container = container
      ..database = database
      ..crashReporter =
          container.read(crashReporterProvider) as RecordingCrashReporter
      ..core = container.read(syncCoreProvider);
    await harness.core.adoptAccount(accountId);
    return harness;
  }

  final List<TestDataType> dataTypes;
  DateTime now = DateTime.utc(2026, 11, 26, 12);
  final random = FixedRandom(0.5);
  final server = FakeSyncServer();
  late final ProviderContainer container;
  late final AppDatabase database;
  late final RecordingCrashReporter crashReporter;
  late final SyncCore core;

  Map<String, Object?> response({
    required String token,
    List<Object?> results = const [],
    List<Object?> scopes = const [],
    List<Object?> removedScopes = const [],
    bool coversAllScopes = false,
    DateTime? serverTime,
    int maxRequestBytes = 1000000,
    int pollIntervalSeconds = 60,
    bool hasMore = false,
  }) => {
    'results': results,
    'scopes': scopes,
    'removedScopes': removedScopes,
    'coversAllScopes': coversAllScopes,
    'token': token,
    'hasMore': hasMore,
    'serverTime': (serverTime ?? now).millisecondsSinceEpoch,
    'settings': {
      'pollIntervalSeconds': pollIntervalSeconds,
      'maxCommandsPerRequest': 500,
      'maxRequestBytes': maxRequestBytes,
    },
  };

  /// Answers the next request with every sent command applied.
  void answerApplyingAll({required String token, int? maxRequestBytes}) =>
      server.answer(
        (requestBody) => response(
          token: token,
          maxRequestBytes: maxRequestBytes ?? 1000000,
          results: [
            for (final commandId in sentCommandIds(requestBody))
              commandResult(commandId, 'applied'),
          ],
        ),
      );

  /// Answers the next request as the first round with the personal scope and
  /// [scopes].
  void answerFirstRound({
    List<Object?> records = const [],
    List<Object?> scopes = const [],
    int maxRequestBytes = 1000000,
    int pollIntervalSeconds = 60,
  }) => server.answer(
    (_) => response(
      token: firstToken,
      coversAllScopes: true,
      maxRequestBytes: maxRequestBytes,
      pollIntervalSeconds: pollIntervalSeconds,
      scopes: [
        scopeBlock(personalScopeId, fullState: true, records: records),
        ...scopes,
      ],
    ),
  );

  /// Completes the first round with the personal scope and [scopes].
  Future<void> completeFirstRound({
    List<Object?> records = const [],
    List<Object?> scopes = const [],
    int maxRequestBytes = 1000000,
  }) async {
    answerFirstRound(
      records: records,
      scopes: scopes,
      maxRequestBytes: maxRequestBytes,
    );
    expect(await core.runRound(), roundOutcome);
  }

  Future<void> create(
    TestDataType dataType,
    String recordId,
    String value, {
    String scopeId = personalScopeId,
  }) => core.write(
    (transaction) => transaction.create(
      dataType: dataType,
      scopeId: scopeId,
      recordId: recordId,
      fields: {dataType.field: value},
    ),
  );

  Future<void> setField(
    TestDataType dataType,
    String recordId,
    String value, {
    String scopeId = personalScopeId,
  }) => core.write(
    (transaction) => transaction.setFields(
      dataType: dataType,
      scopeId: scopeId,
      recordId: recordId,
      fields: {dataType.field: value},
    ),
  );

  Future<void> delete(
    TestDataType dataType,
    String recordId, {
    String scopeId = personalScopeId,
  }) => core.write(
    (transaction) => transaction.delete(
      dataType: dataType,
      scopeId: scopeId,
      recordId: recordId,
    ),
  );

  Future<SyncState> readState() =>
      database.select(database.syncStates).getSingle();

  Future<List<SyncCommand>> readCommands() => (database.select(
    database.syncCommands,
  )..orderBy([(command) => OrderingTerm.asc(command.sequence)])).get();

  Future<List<String>> readScopeIds() async => [
    for (final scope in await database.select(database.syncScopes).get())
      scope.scopeId,
  ];

  /// Everything a round may change, to compare before and after.
  Future<Map<String, Object?>> snapshot() async => {
    'state': (await readState()).toJson(),
    'commands': [for (final command in await readCommands()) command.toJson()],
    'scopes': await readScopeIds(),
    'members': [
      for (final member in await database.select(database.syncMembers).get())
        member.toJson(),
    ],
    'notices': [
      for (final notice in await database.select(database.syncNotices).get())
        notice.toJson(),
    ],
    for (final dataType in dataTypes)
      dataType.name: await dataType.readRecords(database),
  };
}

/// Answers each request with the next scripted response and fails requests
/// that have none.
class FakeSyncServer {
  final _responders = Queue<Future<ResponseBody> Function(RequestBody)>();
  late final adapter = RecordingHttpClientAdapter(respond: _respond);

  List<RequestOptions> get requests => adapter.requests;

  List<RequestBody> get requestBodies => [
    for (final request in adapter.requests) request.data as RequestBody,
  ];

  void answer(ResponseJson build) =>
      _responders.add((requestBody) async => _json(build(requestBody), 200));

  void answerRaw(String body, {int status = 200}) =>
      _responders.add((_) async => ResponseBody.fromString(body, status));

  void answerProblem(
    int status, {
    String? code,
    Map<String, Object?> fields = const {},
    Map<String, List<String>> headers = const {},
  }) => _responders.add(
    (_) async => _json(
      {
        'type': ?(code == null ? null : 'tag:murmli.de,2026:$code'),
        'status': status,
        ...fields,
      },
      status,
      headers: headers,
    ),
  );

  void failWith(Object error) => _responders.add((_) async => throw error);

  /// Holds the response until [HeldResponse.release], so a test can act
  /// while the request is in flight.
  HeldResponse answerAfterRelease(ResponseJson build, {int status = 200}) {
    final held = HeldResponse._();
    _responders.add((requestBody) async {
      held._arrived.complete();
      await held._released.future;
      return _json(build(requestBody), status);
    });
    return held;
  }

  Future<ResponseBody> _respond(RequestOptions request) {
    if (_responders.isEmpty) {
      throw StateError('No response scripted for ${request.path}');
    }
    return _responders.removeFirst()(request.data as RequestBody);
  }

  ResponseBody _json(
    Object? json,
    int status, {
    Map<String, List<String>> headers = const {},
  }) => ResponseBody.fromString(jsonEncode(json), status, headers: headers);
}

/// Answers every draw with [value], so random waits are predictable.
class FixedRandom implements Random {
  FixedRandom(this.value);

  final double value;

  @override
  double nextDouble() => value;

  @override
  int nextInt(int max) => (value * max).floor();

  @override
  bool nextBool() => value >= 0.5;
}

class HeldResponse {
  HeldResponse._();

  final _arrived = Completer<void>();
  final _released = Completer<void>();

  Future<void> get arrived => _arrived.future;

  void release() => _released.complete();
}

List<String> sentCommandIds(RequestBody requestBody) => [
  for (final command
      in (requestBody['commands']! as List<Object?>)
          .cast<Map<String, Object?>>())
    command['id']! as String,
];

Map<String, Object?> scopeBlock(
  String scopeId, {
  String kind = 'personal',
  bool fullState = false,
  List<Object?> records = const [],
  List<Object?> deletedRecords = const [],
  List<Object?> members = const [],
}) => {
  'scopeId': scopeId,
  'kind': kind,
  'fullState': fullState,
  'records': records,
  'deletedRecords': deletedRecords,
  'members': members,
};

Map<String, Object?> serverRecord(
  TestDataType dataType,
  String recordId,
  String value,
) => {
  'dataType': dataType.name,
  'id': recordId,
  'fields': {dataType.field: value},
};

Map<String, Object?> serverMember(
  String memberId, {
  String name = 'Member',
  bool isOwn = false,
}) => {
  'memberId': memberId,
  'name': name,
  'role': isOwn ? 'owner' : 'member',
  'state': 'active',
  'isOwn': isOwn,
};

Map<String, Object?> commandResult(
  String commandId,
  String status, {
  String? reason,
}) => {'commandId': commandId, 'status': status, 'reason': ?reason};
