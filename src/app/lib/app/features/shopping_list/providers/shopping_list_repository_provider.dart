import 'package:clock/clock.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/api/api_client_provider.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/sync/app_database_provider.dart';
import '../../../core/sync/sync_engine_provider.dart';
import '../shopping_list_repository.dart';

part 'shopping_list_repository_provider.g.dart';

@Riverpod(keepAlive: true)
Future<ShoppingListRepository> shoppingListRepository(Ref ref) async {
  final apiClient = ref.watch(apiClientProvider);
  final fetchJsonWebToken = ref
      .watch(signedInAccountProvider.notifier)
      .fetchJsonWebToken;
  return ShoppingListRepository(
    database: await ref.watch(appDatabaseProvider.future),
    requestSync: ({isDebounced = false}) => ref
        .read(syncEngineProvider)
        .value
        ?.requestSync(isDebounced: isDebounced),
    apiClient: apiClient,
    fetchJsonWebToken: fetchJsonWebToken,
    clock: const Clock(),
  );
}
