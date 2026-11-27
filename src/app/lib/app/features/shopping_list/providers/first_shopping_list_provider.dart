import 'dart:async';

import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/build_mode/debug_build_provider.dart';
import '../../../core/database/app_database.dart';
import '../../../core/database/app_database_provider.dart';
import '../../../core/flavor/flavor.dart';
import '../../../core/flavor/flavor_provider.dart';
import '../../../core/identifiers/new_uuid.dart';
import '../../../core/time/clock_provider.dart';
import '../sample_shopping_list.dart';

part 'first_shopping_list_provider.g.dart';

@Riverpod(keepAlive: true)
class FirstShoppingList extends _$FirstShoppingList {
  @override
  void build() {
    ref.listen(signedInAccountProvider, (previous, account) {
      if (account != null) {
        unawaited(_createListIfMissing());
      }
    }, fireImmediately: true);
  }

  AppDatabase get _database => ref.read(appDatabaseProvider);

  Future<void> _createListIfMissing() => _database.transaction(() async {
    if (await _existingListId() != null) {
      return;
    }
    final listId = newUuid();
    await _database
        .into(_database.shoppingLists)
        .insert(ShoppingListsCompanion.insert(id: listId, name: ''));
    if (ref.read(debugBuildProvider) ||
        ref.read(flavorProvider) == Flavor.local) {
      await _insertSampleItems(listId);
    }
  });

  Future<void> resetSampleData() => _database.transaction(() async {
    final listId = await _existingListId();
    if (listId == null) {
      return;
    }
    await (_database.delete(
      _database.shoppingListItems,
    )..where((item) => item.scopeId.equals(listId))).go();
    await _insertSampleItems(listId);
  });

  Future<String?> _existingListId() async {
    final list = await (_database.select(
      _database.shoppingLists,
    )..limit(1)).getSingleOrNull();
    return list?.id;
  }

  Future<void> _insertSampleItems(String listId) =>
      insertSampleShoppingListItems(
        _database,
        listId: listId,
        now: ref.read(appClockProvider).now(),
      );
}
