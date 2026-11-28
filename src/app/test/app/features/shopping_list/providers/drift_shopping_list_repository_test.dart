import 'dart:io';

import 'package:clock/clock.dart';
import 'package:drift/drift.dart' show Value, driftRuntimeOptions;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/measurement_unit.dart';
import 'package:app/app/features/shopping_list/models/recipe_share.dart';
import 'package:app/app/features/shopping_list/models/shopping_list.dart';
import 'package:app/app/features/shopping_list/models/shopping_list_item.dart';
import 'package:app/app/features/shopping_list/providers/drift_shopping_list_repository.dart';

void main() {
  const listId = 'list';
  final creationTime = DateTime.utc(2026, 11, 27, 9);
  late DateTime now;
  late AppDatabase database;
  late DriftShoppingListRepository repository;
  late List<ShoppingList?> emittedLists;

  DriftShoppingListRepository repositoryOf(AppDatabase database) =>
      DriftShoppingListRepository(database, Clock(() => now));

  Future<ShoppingList?> latestList() async {
    await pumpEventQueue();
    return emittedLists.last;
  }

  Future<ShoppingListItem> latestItem(String itemId) async =>
      (await latestList())!.items.singleWhere((item) => item.id == itemId);

  Future<String> addItemWithShares({
    required String name,
    required double? quantity,
    required List<RecipeShare> shares,
  }) async {
    final itemId = await repository.addItem(
      listId: listId,
      name: name,
      quantity: quantity,
      unit: MeasurementUnit.gram,
      category: MarketCategory.dairy,
    );
    for (final share in shares) {
      await database
          .into(database.shoppingListItemRecipes)
          .insert(
            ShoppingListItemRecipesCompanion.insert(
              itemId: itemId,
              recipeId: share.recipeId,
              recipeTitle: share.recipeTitle,
              quantity: Value(share.quantity),
            ),
          );
    }
    return itemId;
  }

  Future<void> insertList(AppDatabase database) => database
      .into(database.shoppingLists)
      .insert(ShoppingListsCompanion.insert(id: listId, name: ''));

  setUp(() async {
    now = creationTime;
    database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);
    repository = repositoryOf(database);
    emittedLists = [];
    final subscription = repository.watchList().listen(emittedLists.add);
    addTearDown(subscription.cancel);
  });

  group('DriftShoppingListRepository', () {
    test('watchList without a list emits null', () async {
      expect(await latestList(), isNull);
    });

    test('watchList after the list is created emits the empty list', () async {
      await insertList(database);

      final list = await latestList();
      expect(list?.id, listId);
      expect(list?.items, isEmpty);
    });

    test('addItem emits the list with the new unchecked item, created and changed now', () async {
      await insertList(database);

      final itemId = await repository.addItem(
        listId: listId,
        name: 'Rinderhack',
        quantity: 500,
        unit: MeasurementUnit.gram,
        category: MarketCategory.meatAndFish,
      );

      final item = await latestItem(itemId);
      expect(item.name, 'Rinderhack');
      expect(item.quantity, 500);
      expect(item.unit, MeasurementUnit.gram);
      expect(item.category, MarketCategory.meatAndFish);
      expect(item.checked, isFalse);
      expect(item.createdAt, creationTime);
      expect(item.changedAt, creationTime);
      expect(item.recipeShares, isEmpty);
    });

    test('addItem stores category and unit as their keys', () async {
      await insertList(database);

      await repository.addItem(
        listId: listId,
        name: 'Rinderhack',
        quantity: 500,
        unit: MeasurementUnit.gram,
        category: MarketCategory.meatAndFish,
      );

      final row = await database.select(database.shoppingListItems).getSingle();
      expect(row.category, 'meatAndFish');
      expect(row.unit, 'gram');
    });

    test('watchList with unknown category and unit keys reads miscellaneous without unit', () async {
      await insertList(database);
      await database
          .into(database.shoppingListItems)
          .insert(
            ShoppingListItemsCompanion.insert(
              id: 'item',
              scopeId: listId,
              name: 'Unbekannt',
              quantity: const Value(1),
              unit: const Value('bushel'),
              category: 'garden',
              language: 'de',
            ),
          );

      final item = await latestItem('item');
      expect(item.category, MarketCategory.miscellaneous);
      expect(item.unit, isNull);
    });

    test(
      'watchList orders the items by creation time and then by id',
      () async {
        await insertList(database);
        now = creationTime.add(const Duration(minutes: 1));
        final laterItemId = await repository.addItem(
          listId: listId,
          name: 'Später',
          category: MarketCategory.fruits,
        );
        now = creationTime;
        final earlierItemId = await repository.addItem(
          listId: listId,
          name: 'Früher',
          category: MarketCategory.fruits,
        );

        final itemIds = (await latestList())!.items.map((item) => item.id);
        expect(itemIds, [earlierItemId, laterItemId]);
      },
    );

    test('updateItem emits the changed item with a new change time and keeps creation time and recipe shares', () async {
      await insertList(database);
      const share = RecipeShare(
        recipeId: 'goulash',
        recipeTitle: 'Gulasch',
        quantity: 200,
      );
      final itemId = await addItemWithShares(
        name: 'Sauerrahm',
        quantity: 500,
        shares: [share],
      );
      final changeTime = creationTime.add(const Duration(hours: 1));
      now = changeTime;

      await repository.updateItem(
        itemId,
        name: 'Schmand',
        quantity: 1,
        unit: MeasurementUnit.package,
        category: MarketCategory.refrigerated,
      );

      final item = await latestItem(itemId);
      expect(item.name, 'Schmand');
      expect(item.quantity, 1);
      expect(item.unit, MeasurementUnit.package);
      expect(item.category, MarketCategory.refrigerated);
      expect(item.createdAt, creationTime);
      expect(item.changedAt, changeTime);
      expect(item.recipeShares, [share]);
    });

    test('setChecked emits the item checked and unchecked again with a new change time', () async {
      await insertList(database);
      final itemId = await repository.addItem(
        listId: listId,
        name: 'Milch',
        category: MarketCategory.dairy,
      );
      final checkTime = creationTime.add(const Duration(minutes: 5));
      now = checkTime;

      await repository.setChecked(itemId, checked: true);
      final checkedItem = await latestItem(itemId);
      await repository.setChecked(itemId, checked: false);

      expect(checkedItem.checked, isTrue);
      expect(checkedItem.changedAt, checkTime);
      expect((await latestItem(itemId)).checked, isFalse);
    });

    test('deleteItem emits the list without the item and removes its recipe shares', () async {
      await insertList(database);
      final itemId = await addItemWithShares(
        name: 'Sauerrahm',
        quantity: 500,
        shares: const [
          RecipeShare(
            recipeId: 'goulash',
            recipeTitle: 'Gulasch',
            quantity: 200,
          ),
        ],
      );

      await repository.deleteItem(itemId);

      expect((await latestList())!.items, isEmpty);
      expect(
        await database.select(database.shoppingListItemRecipes).get(),
        isEmpty,
      );
    });

    test('recipe shares are read per recipe with their quantity and the own quantity is the rest of the total', () async {
      await insertList(database);
      final sourCreamId = await addItemWithShares(
        name: 'Sauerrahm',
        quantity: 500,
        shares: const [
          RecipeShare(
            recipeId: 'goulash',
            recipeTitle: 'Gulasch',
            quantity: 200,
          ),
          RecipeShare(
            recipeId: 'baked-potatoes',
            recipeTitle: 'Ofenkartoffeln',
            quantity: 300,
          ),
        ],
      );
      final eggsId = await addItemWithShares(
        name: 'Eier',
        quantity: 6,
        shares: const [
          RecipeShare(recipeId: 'goulash', recipeTitle: 'Gulasch', quantity: 2),
        ],
      );

      final sourCream = await latestItem(sourCreamId);
      expect(
        sourCream.recipeShares.map(
          (share) => (share.recipeTitle, share.quantity),
        ),
        unorderedEquals([('Gulasch', 200.0), ('Ofenkartoffeln', 300.0)]),
      );
      expect(sourCream.ownQuantity, 0);
      expect((await latestItem(eggsId)).ownQuantity, 4);
    });

    test('ownQuantity with recipe shares above the total is zero and without a total is null', () async {
      await insertList(database);
      const share = RecipeShare(
        recipeId: 'goulash',
        recipeTitle: 'Gulasch',
        quantity: 300,
      );
      final exceededId = await addItemWithShares(
        name: 'Sauerrahm',
        quantity: 200,
        shares: [share],
      );
      final withoutTotalId = await addItemWithShares(
        name: 'Salz',
        quantity: null,
        shares: [share],
      );

      expect((await latestItem(exceededId)).ownQuantity, 0);
      expect((await latestItem(withoutTotalId)).ownQuantity, isNull);
    });

    test(
      'deleteCheckedItems emits the list with only the unchecked items',
      () async {
        await insertList(database);
        final checkedItemId = await repository.addItem(
          listId: listId,
          name: 'Milch',
          category: MarketCategory.dairy,
        );
        final uncheckedItemId = await repository.addItem(
          listId: listId,
          name: 'Brot',
          category: MarketCategory.bakery,
        );
        await repository.setChecked(checkedItemId, checked: true);

        await repository.deleteCheckedItems(listId);

        expect((await latestList())!.items.map((item) => item.id), [
          uncheckedItemId,
        ]);
      },
    );

    test('deleteRecipeItems removes pure recipe items, keeps the own quantity of mixed items without recipes and keeps own items', () async {
      await insertList(database);
      const goulash = RecipeShare(
        recipeId: 'goulash',
        recipeTitle: 'Gulasch',
        quantity: 2,
      );
      final eggsId = await addItemWithShares(
        name: 'Eier',
        quantity: 6,
        shares: [goulash],
      );
      await addItemWithShares(name: 'Paprika', quantity: 2, shares: [goulash]);
      await addItemWithShares(
        name: 'Kümmel',
        quantity: null,
        shares: [goulash],
      );
      final ownItemId = await repository.addItem(
        listId: listId,
        name: 'Brot',
        category: MarketCategory.bakery,
      );
      final deletionTime = creationTime.add(const Duration(hours: 2));
      now = deletionTime;

      await repository.deleteRecipeItems(listId);

      final items = (await latestList())!.items;
      expect(
        items.map((item) => item.id),
        unorderedEquals([eggsId, ownItemId]),
      );
      final eggs = items.singleWhere((item) => item.id == eggsId);
      expect(eggs.quantity, 4);
      expect(eggs.recipeShares, isEmpty);
      expect(eggs.changedAt, deletionTime);
    });

    test(
      'deleteAllItems emits the empty list and keeps the category order',
      () async {
        await insertList(database);
        await repository.addItem(
          listId: listId,
          name: 'Milch',
          category: MarketCategory.dairy,
        );
        await repository.setCategoryOrder([MarketCategory.frozen]);

        await repository.deleteAllItems(listId);

        expect((await latestList())!.items, isEmpty);
        expect(
          (await repository.watchCategoryOrder().first).first,
          MarketCategory.frozen,
        );
      },
    );

    test('watchCategoryOrder without a stored order emits all categories in the default order', () async {
      expect(
        await repository.watchCategoryOrder().first,
        MarketCategory.values,
      );
    });

    test('setCategoryOrder emits the stored categories first and the missing ones after them in the default order', () async {
      await repository.setCategoryOrder([
        MarketCategory.frozen,
        MarketCategory.bakery,
      ]);

      expect(await repository.watchCategoryOrder().first, [
        MarketCategory.frozen,
        MarketCategory.bakery,
        ...MarketCategory.values.where(
          (category) =>
              category != MarketCategory.frozen &&
              category != MarketCategory.bakery,
        ),
      ]);
    });

    test(
      'resetCategoryOrder after a stored order emits the default order',
      () async {
        await repository.setCategoryOrder([MarketCategory.frozen]);

        await repository.resetCategoryOrder();

        expect(
          await repository.watchCategoryOrder().first,
          MarketCategory.values,
        );
      },
    );

    test(
      'data written before the database is closed is read after reopening it',
      () async {
        driftRuntimeOptions.dontWarnAboutMultipleDatabases = true;
        addTearDown(
          () => driftRuntimeOptions.dontWarnAboutMultipleDatabases = false,
        );
        final directory = await Directory.systemTemp.createTemp('murmli');
        addTearDown(() => directory.delete(recursive: true));
        final file = File('${directory.path}/murmli.sqlite');
        final firstDatabase = AppDatabase(NativeDatabase(file));
        await insertList(firstDatabase);
        final itemId = await repositoryOf(firstDatabase).addItem(
          listId: listId,
          name: 'Milch',
          category: MarketCategory.dairy,
        );
        await firstDatabase.close();

        final reopenedDatabase = AppDatabase(NativeDatabase(file));
        addTearDown(reopenedDatabase.close);
        final list = await repositoryOf(reopenedDatabase).watchList().first;

        expect(list?.items.single.id, itemId);
        expect(list?.items.single.name, 'Milch');
      },
    );
  });
}
