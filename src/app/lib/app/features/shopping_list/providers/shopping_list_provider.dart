import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:powersync/powersync.dart' show SyncStatus;

import '../../../../i18n/strings.g.dart';
import '../../../core/sync/app_database.dart';
import '../../../core/sync/sync_status_provider.dart';
import 'shopping_list_repository_provider.dart';

// Declared without riverpod_generator: it cannot resolve the drift data
// classes, which only exist after source_gen combined the drift part.
final shoppingListProvider = StreamProvider.autoDispose<ShoppingList?>((
  ref,
) async* {
  final repository = await ref.watch(shoppingListRepositoryProvider.future);
  ref.listen(syncStatusProvider, (_, status) {
    if (status case AsyncData(value: SyncStatus(hasSynced: true))) {
      unawaited(repository.ensureList(t.shoppingList.defaultName));
    }
  }, fireImmediately: true);
  yield* repository.watchFirstList();
});

final shoppingListItemsProvider = StreamProvider.autoDispose
    .family<List<ShoppingListItem>, String>((ref, listId) async* {
      final repository = await ref.watch(shoppingListRepositoryProvider.future);
      yield* repository.watchItems(listId);
    });
