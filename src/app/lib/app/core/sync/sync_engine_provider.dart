import 'dart:async';

import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../authentication/signed_in_account_provider.dart';
import '../flavor/app_endpoints_provider.dart';
import '../logging/talker_provider.dart';
import 'app_database_provider.dart';
import 'simulated_offline_provider.dart';
import 'sync_engine.dart';
import 'sync_round.dart';

part 'sync_engine_provider.g.dart';

@Riverpod(keepAlive: true)
Future<SyncEngine?> syncEngine(Ref ref) async {
  if (ref.watch(signedInAccountProvider) == null) {
    return null;
  }
  final database = await ref.watch(appDatabaseProvider.future);
  if (!ref.mounted) {
    return null;
  }
  // The engine retries endlessly while offline, the error dialog of
  // apiClientProvider would reappear for every attempt.
  final client = Dio(
    BaseOptions(baseUrl: ref.watch(appEndpointsProvider).api.toString()),
  );
  final fetchJsonWebToken = ref
      .watch(signedInAccountProvider.notifier)
      .fetchJsonWebToken;
  final talker = ref.watch(talkerProvider);
  final engine = SyncEngine(
    round: SyncRound(
      database: database,
      client: client,
      fetchJsonWebToken: fetchJsonWebToken,
      talker: talker,
    ),
    client: client,
    fetchJsonWebToken: fetchJsonWebToken,
    talker: talker,
  );
  ref
    ..onDispose(engine.dispose)
    ..listen(simulatedOfflineProvider, (_, isSimulatedOffline) {
      if (isSimulatedOffline) {
        unawaited(engine.stop());
      } else {
        engine.start();
      }
    }, fireImmediately: true);
  return engine;
}

@riverpod
Stream<SyncEngineStatus> syncEngineStatus(Ref ref) async* {
  final engine = await ref.watch(syncEngineProvider.future);
  if (engine == null) {
    yield stoppedSyncEngineStatus;
    return;
  }
  yield* engine.watchStatus();
}

@riverpod
Stream<int> pendingCommandCount(Ref ref) async* {
  final database = await ref.watch(appDatabaseProvider.future);
  yield* database.pendingCommands.count().watchSingle();
}
