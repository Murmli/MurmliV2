import 'package:app/app/core/sync/app_database.dart';
import 'package:app/app/core/sync/sync_command.dart';
import 'package:app/app/core/sync/sync_round.dart';
import 'package:app/app/features/shopping_list/shopping_list_repository.dart';
import 'package:clock/clock.dart';
import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:talker/talker.dart';

import 'fault_injecting_http_client_adapter.dart';
import 'json_web_token.dart';
import 'simulation_configuration.dart';

class SimulatedUser {
  const SimulatedUser({
    required this.name,
    required this.subject,
    required this.id,
  });

  final String name;
  final String subject;
  final String id;
}

typedef DeviceTables = ({
  List<ShoppingList> lists,
  List<ShoppingListMember> members,
  List<ShoppingListItem> items,
  List<PendingCommand> pendingCommands,
});

typedef SyncAttempt = ({
  SyncFault fault,
  bool isOffline,
  SyncRoundOutcome outcome,
  RecordedSyncResponse? response,
});

class SimulatedDevice {
  SimulatedDevice({
    required this.name,
    required this.user,
    required SimulationConfiguration configuration,
    required DateTime Function() currentTime,
    required void Function(RecordedSyncResponse response) onSyncResponse,
  }) {
    _adapter = FaultInjectingHttpClientAdapter(
      onSyncRequest: () => _onSyncRequest(),
      onSyncResponse: onSyncResponse,
    );
    _client = Dio(BaseOptions(baseUrl: configuration.apiUrl))
      ..httpClientAdapter = _adapter;
    Future<String?> fetchJsonWebToken() async =>
        createJsonWebToken(configuration, user.subject);
    _round = SyncRound(
      database: database,
      client: _client,
      fetchJsonWebToken: fetchJsonWebToken,
      talker: Talker(settings: TalkerSettings(useConsoleLogs: false)),
    );
    repository = ShoppingListRepository(
      database: database,
      requestSync: ({isDebounced = false}) {},
      apiClient: _client,
      fetchJsonWebToken: fetchJsonWebToken,
      clock: Clock(currentTime),
    );
  }

  final String name;
  final SimulatedUser user;
  final database = AppDatabase(NativeDatabase.memory());
  late final ShoppingListRepository repository;
  late final FaultInjectingHttpClientAdapter _adapter;
  late final Dio _client;
  late final SyncRound _round;
  late Future<void> Function() _onSyncRequest;

  bool get isOffline => _adapter.isOffline;

  set isOffline(bool isOffline) => _adapter.isOffline = isOffline;

  /// Starts one round per fault at the same time. [duringRound] runs once,
  /// when the first sync request reaches the adapter, and every request waits
  /// for it before it is sent.
  Future<List<SyncAttempt>> sync(
    List<SyncFault> faults, {
    Future<void> Function()? duringRound,
  }) async {
    Future<void>? writesDuringRound;
    _onSyncRequest = () async {
      await (writesDuringRound ??= duringRound?.call());
    };
    _adapter.faults.addAll(faults);
    final cancelTokens = [for (final _ in faults) CancelToken()];
    final outcomes = await Future.wait([
      for (final cancelToken in cancelTokens)
        _round.run(cancelToken: cancelToken),
    ]);
    // The adapter turns an error of the writes into a failed round.
    await writesDuringRound;
    final exchanges = [
      for (final cancelToken in cancelTokens)
        _adapter.exchanges.remove(cancelToken)!,
    ];
    return [
      for (final (index, (:fault, :response)) in exchanges.indexed)
        (
          fault: fault,
          isOffline: isOffline,
          outcome: outcomes[index],
          response: response,
        ),
    ];
  }

  Future<DeviceTables> readTables() async => (
    lists: await (database.select(
      database.shoppingLists,
    )..orderBy([(list) => OrderingTerm.asc(list.name)])).get(),
    members: await (database.select(
      database.shoppingListMembers,
    )..orderBy([(member) => OrderingTerm.asc(member.id)])).get(),
    items: await (database.select(
      database.shoppingListItems,
    )..orderBy([(item) => OrderingTerm.asc(item.name)])).get(),
    pendingCommands: await (database.select(
      database.pendingCommands,
    )..orderBy([(command) => OrderingTerm.asc(command.sequence)])).get(),
  );

  /// Re-applies the pending commands in sequence order, like the rebase at the
  /// end of a sync round, and returns the tables before and after.
  Future<({DeviceTables before, DeviceTables after})> replayPendingCommands() =>
      database.transaction(() async {
        final before = await readTables();
        for (final pending in before.pendingCommands) {
          await applySyncCommand(database, SyncCommand.fromPending(pending));
        }
        return (before: before, after: await readTables());
      });

  Future<void> close() async {
    _client.close(force: true);
    await database.close();
  }
}
