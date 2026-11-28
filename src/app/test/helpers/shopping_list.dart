import 'package:drift/drift.dart';

import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/measurement_unit.dart';

class TestShoppingList {
  const TestShoppingList({
    this.id = 'list',
    this.name = '',
    this.items = const [],
  });

  final String id;
  final String name;
  final List<TestShoppingListItem> items;
}

class TestShoppingListItem {
  const TestShoppingListItem(
    this.name,
    this.category, {
    this.quantity,
    this.unit,
    this.checked = false,
    this.recipeShares = const [],
  });

  final String name;
  final MarketCategory category;
  final double? quantity;
  final MeasurementUnit? unit;
  final bool checked;
  final List<(String recipeTitle, double quantity)> recipeShares;
}

/// Stores [list] as the first sync brings it, with its recipe shares, in the
/// order of its items.
Future<void> storeShoppingList(AppDatabase database, TestShoppingList list) =>
    database.batch((batch) {
      batch.insert(
        database.shoppingLists,
        ShoppingListsCompanion.insert(id: list.id, name: list.name),
      );
      for (final (index, item) in list.items.indexed) {
        final itemId = '${list.id}-item-$index';
        batch.insert(
          database.shoppingListItems,
          ShoppingListItemsCompanion.insert(
            id: itemId,
            scopeId: list.id,
            name: item.name,
            quantity: Value(item.quantity),
            unit: Value(item.unit?.key),
            category: item.category.key,
            checked: Value(item.checked),
            language: 'de',
            createdAt: Value(index),
            changedAt: Value(index),
          ),
        );
        for (final (recipeTitle, quantity) in item.recipeShares) {
          batch.insert(
            database.shoppingListItemRecipes,
            ShoppingListItemRecipesCompanion.insert(
              itemId: itemId,
              recipeId: recipeTitle,
              recipeTitle: recipeTitle,
              quantity: Value(quantity),
            ),
          );
        }
      }
    });

const goulash = 'Gulasch';
const bakedPotatoes = 'Ofenkartoffeln';

const sampleShoppingListItems = [
  TestShoppingListItem(
    'Paprika',
    MarketCategory.vegetables,
    quantity: 3,
    unit: MeasurementUnit.piece,
    recipeShares: [(goulash, 3)],
  ),
  TestShoppingListItem(
    'Zwiebeln',
    MarketCategory.vegetables,
    quantity: 1,
    unit: MeasurementUnit.kilogram,
    recipeShares: [(goulash, 1)],
  ),
  TestShoppingListItem(
    'Kartoffeln',
    MarketCategory.vegetables,
    quantity: 2,
    unit: MeasurementUnit.kilogram,
    recipeShares: [(bakedPotatoes, 2)],
  ),
  TestShoppingListItem(
    'Äpfel',
    MarketCategory.fruits,
    quantity: 6,
    unit: MeasurementUnit.piece,
  ),
  TestShoppingListItem(
    'Bananen',
    MarketCategory.fruits,
    quantity: 5,
    unit: MeasurementUnit.piece,
    checked: true,
  ),
  TestShoppingListItem(
    'Vollkornbrot',
    MarketCategory.bakery,
    quantity: 1,
    unit: MeasurementUnit.piece,
  ),
  TestShoppingListItem(
    'Eier',
    MarketCategory.refrigerated,
    quantity: 6,
    unit: MeasurementUnit.piece,
    recipeShares: [(goulash, 2)],
  ),
  TestShoppingListItem(
    'Sauerrahm',
    MarketCategory.dairy,
    quantity: 500,
    unit: MeasurementUnit.gram,
    recipeShares: [(goulash, 200), (bakedPotatoes, 300)],
  ),
  TestShoppingListItem(
    'Milch',
    MarketCategory.dairy,
    quantity: 1,
    unit: MeasurementUnit.liter,
    checked: true,
  ),
  TestShoppingListItem(
    'Rindergulasch',
    MarketCategory.meatAndFish,
    quantity: 800,
    unit: MeasurementUnit.gram,
    recipeShares: [(goulash, 800)],
  ),
  TestShoppingListItem(
    'Spaghetti',
    MarketCategory.grainsAndPasta,
    quantity: 500,
    unit: MeasurementUnit.gram,
  ),
  TestShoppingListItem(
    'Gehackte Tomaten',
    MarketCategory.cannedGoods,
    quantity: 2,
    unit: MeasurementUnit.can,
  ),
  TestShoppingListItem(
    'Paprikapulver',
    MarketCategory.spices,
    quantity: 2,
    unit: MeasurementUnit.tablespoon,
    recipeShares: [(goulash, 2)],
  ),
  TestShoppingListItem('Olivenöl', MarketCategory.vinegarAndOils),
  TestShoppingListItem(
    'Mineralwasser',
    MarketCategory.beverages,
    quantity: 6,
    unit: MeasurementUnit.liter,
  ),
  TestShoppingListItem('Spülmittel', MarketCategory.householdGoods),
];
