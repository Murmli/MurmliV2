import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/database/app_database_provider.dart';
import '../../../core/sync/sync_core.dart';
import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import '../models/shopping_list.dart';
import 'drift_shopping_list_repository.dart';

part 'shopping_list_repository.g.dart';

@Riverpod(keepAlive: true)
ShoppingListRepository shoppingListRepository(Ref ref) =>
    DriftShoppingListRepository(
      ref.watch(appDatabaseProvider),
      ref.watch(syncCoreProvider),
    );

/// Writes to the list and its items go through the sync core and throw
/// [SyncNotReadyException] before the first completed sync and
/// [ShoppingListLimitExceededException] over a limit of the server, both
/// without any change. The category order stays on the device.
abstract interface class ShoppingListRepository {
  /// The first list by name, until the app can switch between lists.
  Stream<ShoppingList?> watchList();

  Stream<List<MarketCategory>> watchCategoryOrder();

  Future<String> addItem({
    required String listId,
    required String name,
    required MarketCategory category,
    double? quantity,
    MeasurementUnit? unit,
  });

  Future<void> updateItem(
    String itemId, {
    required String name,
    required double? quantity,
    required MeasurementUnit? unit,
    required MarketCategory category,
  });

  Future<void> setChecked(String itemId, {required bool checked});

  Future<void> deleteItem(String itemId);

  Future<void> setCategoryOrder(List<MarketCategory> categories);

  Future<void> resetCategoryOrder();

  Future<void> deleteCheckedItems(String listId);

  // Items with an own quantity beyond their recipe shares stay with exactly
  // that quantity and without recipes.
  Future<void> deleteRecipeItems(String listId);

  Future<void> deleteAllItems(String listId);
}

enum ShoppingListLimit { itemName, quantity, itemsPerList }

class ShoppingListLimitExceededException implements Exception {
  const ShoppingListLimitExceededException(this.limit);

  final ShoppingListLimit limit;

  @override
  String toString() => 'ShoppingListLimitExceededException: ${limit.name}';
}
