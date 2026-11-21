import 'package:app/app/core/sync/app_database.dart';
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
  String fault,
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
      onSyncResponse: (response) {
        _lastSyncResponse = response;
        onSyncResponse(response);
      },
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
  RecordedSyncResponse? _lastSyncResponse;

  bool get isOffline => _adapter.isOffline;

  set isOffline(bool isOffline) => _adapter.isOffline = isOffline;

  Future<SyncAttempt> sync(SyncFault fault) async {
    final effectiveFault = isOffline ? 'offline' : fault.name;
    _adapter.fault = fault;
    _lastSyncResponse = null;
    final outcome = await _round.run();
    return (
      fault: effectiveFault,
      outcome: outcome,
      response: _lastSyncResponse,
    );
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

  Future<void> close() async {
    _client.close(force: true);
    await database.close();
  }
}
