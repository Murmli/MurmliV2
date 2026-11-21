import 'package:dio/dio.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../authentication/signed_in_account_provider.dart';
import '../flavor/app_endpoints_provider.dart';
import '../logging/talker_provider.dart';
import 'murmli_backend_connector.dart';
import 'power_sync_database_provider.dart';

part 'sync_connection_provider.g.dart';

@Riverpod(keepAlive: true)
Future<void> syncConnection(Ref ref) async {
  if (ref.watch(signedInAccountProvider) == null) {
    if (ref.read(powerSyncDatabaseProvider.exists)) {
      final database = await ref.read(powerSyncDatabaseProvider.future);
      await database.disconnect();
    }
    return;
  }
  final endpoints = ref.watch(appEndpointsProvider);
  final syncEndpoint = endpoints.sync;
  if (syncEndpoint == null) {
    return;
  }
  final database = await ref.watch(powerSyncDatabaseProvider.future);
  await database.connect(
    connector: MurmliBackendConnector(
      syncEndpoint: syncEndpoint,
      // PowerSync retries failed uploads endlessly while offline, the error
      // dialog of apiClientProvider would reappear for every attempt.
      apiClient: Dio(BaseOptions(baseUrl: endpoints.api.toString())),
      fetchJsonWebToken: ref
          .read(signedInAccountProvider.notifier)
          .fetchJsonWebToken,
      talker: ref.read(talkerProvider),
    ),
  );
}
