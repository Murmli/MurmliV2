import 'dart:convert';

import 'package:drift/drift.dart' show Value;
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/sync/sync_core.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/measurement_unit.dart';
import 'package:app/app/features/shopping_list/models/recipe_share.dart';
import 'package:app/app/features/shopping_list/models/shopping_list.dart';
import 'package:app/app/features/shopping_list/models/shopping_list_item.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_repository.dart';
import 'package:app/app/features/shopping_list/sync/shopping_list_data_type.dart';
import 'package:app/app/features/shopping_list/sync/shopping_list_item_data_type.dart';

import '../../../core/sync/sync_test_setup.dart';

const _listId = '6f1c2a3b-4d5e-4f60-8a71-b2c3d4e5f607';
const _goulash = RecipeShare(
  recipeId: 'goulash',
  recipeTitle: 'Gulasch',
  quantity: 2,
);

Map<String, Object?> _listRecord(String listId, String name) => {
  'dataType': 'shoppingList',
  'id': listId,
  'fields': {'name': name},
};

Map<String, Object?> _itemRecord(
  String itemId, {
  String name = 'Milch',
  num? quantity,
  String? unit,
  String category = 'dairy',
  bool checked = false,
  int createdAt = 1,
}) => {
  'dataType': 'shoppingListItem',
  'id': itemId,
  'fields': {
    'name': name,
    'quantity': quantity,
    'unit': unit,
    'category': category,
    'checked': checked,
    'language': 'de',
    'createdAt': createdAt,
    'changedAt': createdAt,
    'changedBy': 'member',
  },
};

Map<String, Object?> _listBlock({
  String listId = _listId,
  String name = '',
  List<Map<String, Object?>> items = const [],
}) => scopeBlock(
  listId,
  kind: 'shoppingList',
  fullState: true,
  records: [_listRecord(listId, name), ...items],
);

void main() {
  late SyncTestHarness harness;
  late AppDatabase database;
  late ShoppingListRepository repository;
  late List<ShoppingList?> emittedLists;

  final creationTime = DateTime.utc(2026, 11, 26, 12);

  setUp(() async {
    harness = await SyncTestHarness.start(
      dataTypes: const [ShoppingListDataType(), ShoppingListItemDataType()],
    );
    harness.now = creationTime;
    database = harness.database;
    repository = harness.container.read(shoppingListRepositoryProvider);
    emittedLists = [];
    final subscription = repository.watchList().listen(emittedLists.add);
    addTearDown(subscription.cancel);
  });

  Future<ShoppingList?> latestList() async {
    await pumpEventQueue();
    return emittedLists.last;
  }

  Future<ShoppingListItem> latestItem(String itemId) async =>
      (await latestList())!.items.singleWhere((item) => item.id == itemId);

  Future<List<List<Object?>>> readCommands() async => [
    for (final command in await harness.readCommands())
      [
        command.kind,
        command.dataType,
        command.recordId,
        switch (command.fields) {
          final fields? => jsonDecode(fields),
          null => null,
        },
      ],
  ];

  Future<void> insertShares(String itemId, List<RecipeShare> shares) async {
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
  }

  Future<String> addItem(
    String name, {
    MarketCategory category = MarketCategory.dairy,
    double? quantity,
    MeasurementUnit? unit,
    List<RecipeShare> shares = const [],
  }) async {
    final itemId = await repository.addItem(
      listId: _listId,
      name: name,
      category: category,
      quantity: quantity,
      unit: unit,
    );
    await insertShares(itemId, shares);
    return itemId;
  }

  Future<void> completeFirstRoundWithList({
    List<Map<String, Object?>> items = const [],
  }) => harness.completeFirstRound(scopes: [_listBlock(items: items)]);

  test('watchList without a list emits null', () async {
    await harness.completeFirstRound();

    expect(await latestList(), isNull);
  });

  test('watchList with several lists from the server emits the first by '
      'name', () async {
    const otherListId = '0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d';
    await harness.completeFirstRound(
      scopes: [
        _listBlock(name: 'Wochenende'),
        _listBlock(listId: otherListId, name: 'Drogerie'),
      ],
    );

    expect((await latestList())?.name, 'Drogerie');
  });

  test('a write before the first completed sync throws SyncNotReadyException '
      'and changes nothing', () async {
    await expectLater(
      repository.addItem(
        listId: _listId,
        name: 'Milch',
        category: MarketCategory.dairy,
      ),
      throwsA(isA<SyncNotReadyException>()),
    );

    expect(await harness.readCommands(), isEmpty);
    expect(await database.select(database.shoppingListItems).get(), isEmpty);
  });

  group('after the first sync with the list', () {
    setUp(completeFirstRoundWithList);

    test('addItem queues a create with every field and the app language and '
        'shows the item created and changed now', () async {
      final itemId = await addItem(
        'Rinderhack',
        category: MarketCategory.meatAndFish,
        quantity: 500,
        unit: MeasurementUnit.gram,
      );

      expect(await readCommands(), [
        [
          'create',
          'shoppingListItem',
          itemId,
          {
            'name': 'Rinderhack',
            'quantity': 500.0,
            'unit': 'gram',
            'category': 'meatAndFish',
            'checked': false,
            'language': 'de',
          },
        ],
      ]);
      final item = await latestItem(itemId);
      expect(
        (item.name, item.quantity, item.unit, item.category, item.checked),
        (
          'Rinderhack',
          500,
          MeasurementUnit.gram,
          MarketCategory.meatAndFish,
          false,
        ),
      );
      expect((item.createdAt, item.changedAt), (creationTime, creationTime));
    });

    test(
      'watchList orders the items by creation time and then by id',
      () async {
        harness.now = creationTime.add(const Duration(minutes: 10));
        final laterItemId = await addItem('Später');
        harness.now = creationTime;
        final earlierItemId = await addItem('Früher');

        expect((await latestList())!.items.map((item) => item.id), [
          earlierItemId,
          laterItemId,
        ]);
      },
    );

    test('addItem with a name over 256 characters throws for the item name and '
        'changes nothing', () async {
      await expectLater(
        addItem('a' * 257),
        throwsA(
          isA<ShoppingListLimitExceededException>().having(
            (exception) => exception.limit,
            'limit',
            ShoppingListLimit.itemName,
          ),
        ),
      );

      expect(await harness.readCommands(), isEmpty);
      expect((await latestList())!.items, isEmpty);
    });

    test('updateItem with a quantity over the maximum throws for the quantity '
        'and changes nothing', () async {
      final itemId = await addItem('Milch');
      await harness.database.delete(database.syncCommands).go();

      await expectLater(
        repository.updateItem(
          itemId,
          name: 'Milch',
          quantity: 1000000,
          unit: null,
          category: MarketCategory.dairy,
        ),
        throwsA(
          isA<ShoppingListLimitExceededException>().having(
            (exception) => exception.limit,
            'limit',
            ShoppingListLimit.quantity,
          ),
        ),
      );

      expect(await harness.readCommands(), isEmpty);
      expect((await latestItem(itemId)).quantity, isNull);
    });

    test('updateItem changing name, amount and category queues each group and '
        'keeps creation time and recipe shares', () async {
      final itemId = await addItem(
        'Sauerrahm',
        quantity: 500,
        unit: MeasurementUnit.gram,
        shares: const [_goulash],
      );
      final changeTime = creationTime.add(const Duration(hours: 1));
      harness.now = changeTime;

      await repository.updateItem(
        itemId,
        name: 'Schmand',
        quantity: 1,
        unit: MeasurementUnit.package,
        category: MarketCategory.refrigerated,
      );

      expect((await readCommands()).last, [
        'setFields',
        'shoppingListItem',
        itemId,
        {
          'name': 'Schmand',
          'quantity': 1.0,
          'unit': 'package',
          'category': 'refrigerated',
        },
      ]);
      final item = await latestItem(itemId);
      expect(
        (item.name, item.quantity, item.unit, item.category),
        ('Schmand', 1, MeasurementUnit.package, MarketCategory.refrigerated),
      );
      expect((item.createdAt, item.changedAt), (creationTime, changeTime));
      expect(item.recipeShares, [_goulash]);
    });

    for (final (description, quantity, unit, category, fields) in [
      (
        'only the unit queues quantity and unit together',
        500.0,
        MeasurementUnit.kilogram,
        MarketCategory.dairy,
        {'quantity': 500.0, 'unit': 'kilogram'},
      ),
      (
        'only the category queues only the category',
        500.0,
        MeasurementUnit.gram,
        MarketCategory.refrigerated,
        {'category': 'refrigerated'},
      ),
    ]) {
      test('updateItem changing $description', () async {
        final itemId = await addItem(
          'Sauerrahm',
          quantity: 500,
          unit: MeasurementUnit.gram,
        );

        await repository.updateItem(
          itemId,
          name: 'Sauerrahm',
          quantity: quantity,
          unit: unit,
          category: category,
        );

        expect((await readCommands()).last, [
          'setFields',
          'shoppingListItem',
          itemId,
          fields,
        ]);
      });
    }

    test('updateItem without a change queues nothing', () async {
      final itemId = await addItem('Milch', quantity: 1);

      await repository.updateItem(
        itemId,
        name: 'Milch',
        quantity: 1,
        unit: null,
        category: MarketCategory.dairy,
      );

      expect(await harness.readCommands(), hasLength(1));
    });

    test('setChecked queues checked and shows the item checked now and '
        'unchecked again', () async {
      final itemId = await addItem('Milch');
      final checkTime = creationTime.add(const Duration(minutes: 5));
      harness.now = checkTime;

      await repository.setChecked(itemId, checked: true);
      final checkedItem = await latestItem(itemId);
      await repository.setChecked(itemId, checked: false);

      expect((await readCommands()).skip(1), [
        [
          'setFields',
          'shoppingListItem',
          itemId,
          {'checked': true},
        ],
        [
          'setFields',
          'shoppingListItem',
          itemId,
          {'checked': false},
        ],
      ]);
      expect((checkedItem.checked, checkedItem.changedAt), (true, checkTime));
      expect((await latestItem(itemId)).checked, isFalse);
    });

    test('deleteItem queues a delete and removes the item with its recipe '
        'shares', () async {
      final itemId = await addItem('Sauerrahm', shares: const [_goulash]);

      await repository.deleteItem(itemId);

      expect((await readCommands()).last, [
        'delete',
        'shoppingListItem',
        itemId,
        null,
      ]);
      expect((await latestList())!.items, isEmpty);
      expect(
        await database.select(database.shoppingListItemRecipes).get(),
        isEmpty,
      );
    });

    test('recipe shares are read per recipe with their quantity and the own '
        'quantity is the rest of the total', () async {
      final sourCreamId = await addItem(
        'Sauerrahm',
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
      final eggsId = await addItem(
        'Eier',
        quantity: 6,
        shares: const [_goulash],
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

    test('ownQuantity with recipe shares above the total is zero and without '
        'a total is null', () async {
      const share = RecipeShare(
        recipeId: 'goulash',
        recipeTitle: 'Gulasch',
        quantity: 300,
      );
      final exceededId = await addItem(
        'Sauerrahm',
        quantity: 200,
        shares: const [share],
      );
      final withoutTotalId = await addItem('Salz', shares: const [share]);

      expect((await latestItem(exceededId)).ownQuantity, 0);
      expect((await latestItem(withoutTotalId)).ownQuantity, isNull);
    });

    test('deleteRecipeItems deletes pure recipe items and sets the own amount '
        'of mixed items without recipes', () async {
      final eggsId = await addItem(
        'Eier',
        quantity: 6,
        unit: MeasurementUnit.piece,
        shares: const [_goulash],
      );
      final pepperId = await addItem(
        'Paprika',
        quantity: 2,
        shares: const [_goulash],
      );
      final carawayId = await addItem('Kümmel', shares: const [_goulash]);
      final ownItemId = await addItem('Brot');
      await harness.database.delete(database.syncCommands).go();
      final deletionTime = creationTime.add(const Duration(hours: 2));
      harness.now = deletionTime;

      await repository.deleteRecipeItems(_listId);

      expect(
        await readCommands(),
        unorderedEquals([
          [
            'setFields',
            'shoppingListItem',
            eggsId,
            {'quantity': 4.0, 'unit': 'piece'},
          ],
          ['delete', 'shoppingListItem', pepperId, null],
          ['delete', 'shoppingListItem', carawayId, null],
        ]),
      );
      final items = (await latestList())!.items;
      expect(
        items.map((item) => item.id),
        unorderedEquals([eggsId, ownItemId]),
      );
      final eggs = items.singleWhere((item) => item.id == eggsId);
      expect((eggs.quantity, eggs.changedAt), (4, deletionTime));
      expect(eggs.recipeShares, isEmpty);
    });

    test('deleteAllItems queues a delete per item and keeps the category '
        'order', () async {
      final milkId = await addItem('Milch');
      final breadId = await addItem('Brot');
      await harness.database.delete(database.syncCommands).go();
      await repository.setCategoryOrder([MarketCategory.frozen]);

      await repository.deleteAllItems(_listId);

      expect(
        await readCommands(),
        unorderedEquals([
          ['delete', 'shoppingListItem', milkId, null],
          ['delete', 'shoppingListItem', breadId, null],
        ]),
      );
      expect((await latestList())!.items, isEmpty);
      expect(
        (await repository.watchCategoryOrder().first).first,
        MarketCategory.frozen,
      );
    });

    test('watchCategoryOrder without a stored order emits all categories in '
        'the default order', () async {
      expect(
        await repository.watchCategoryOrder().first,
        MarketCategory.values,
      );
    });

    test('setCategoryOrder emits the stored categories first and the missing '
        'ones after them in the default order and queues nothing', () async {
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
      expect(await harness.readCommands(), isEmpty);
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
  });

  group('after the first sync with items the device knows', () {
    setUp(
      () => completeFirstRoundWithList(
        items: [
          _itemRecord('known-milk', checked: true),
          _itemRecord('known-bread', name: 'Brot', category: 'bakery'),
          _itemRecord(
            'unknown-keys',
            name: 'Unbekannt',
            quantity: 1,
            unit: 'bushel',
            category: 'garden',
          ),
        ],
      ),
    );

    test('an item with an unknown category and unit reads as miscellaneous '
        'without unit and keeps both keys', () async {
      final item = await latestItem('unknown-keys');

      expect((item.category, item.unit), (MarketCategory.miscellaneous, null));
      final row = await (database.select(
        database.shoppingListItems,
      )..where((item) => item.id.equals('unknown-keys'))).getSingle();
      expect((row.category, row.unit), ('garden', 'bushel'));
    });

    test('updateItem of an item with unknown keys changing only the name '
        'queues only the name and keeps both keys', () async {
      await repository.updateItem(
        'unknown-keys',
        name: 'Umbenannt',
        quantity: 1,
        unit: null,
        category: MarketCategory.miscellaneous,
      );

      expect(await readCommands(), [
        [
          'setFields',
          'shoppingListItem',
          'unknown-keys',
          {'name': 'Umbenannt'},
        ],
      ]);
      final row = await (database.select(
        database.shoppingListItems,
      )..where((item) => item.id.equals('unknown-keys'))).getSingle();
      expect(
        (row.name, row.category, row.unit),
        ('Umbenannt', 'garden', 'bushel'),
      );
    });

    test('deleteCheckedItems queues a delete only for the checked items the '
        'device knows', () async {
      await repository.deleteCheckedItems(_listId);

      expect(await readCommands(), [
        ['delete', 'shoppingListItem', 'known-milk', null],
      ]);
      expect(
        (await latestList())!.items.map((item) => item.id),
        unorderedEquals(['known-bread', 'unknown-keys']),
      );
    });

    test('a full state keeps the recipe shares of items it contains and drops '
        'those of items it no longer contains', () async {
      await insertShares('known-bread', const [_goulash]);
      await insertShares('known-milk', const [_goulash]);
      harness.server.answer(
        (_) => harness.response(
          token: 'second-token',
          scopes: [
            _listBlock(
              items: [
                _itemRecord(
                  'known-bread',
                  name: 'Vollkornbrot',
                  category: 'bakery',
                ),
              ],
            ),
          ],
        ),
      );

      await harness.core.runRound();

      final item = (await latestList())!.items.single;
      expect(item.name, 'Vollkornbrot');
      expect(item.recipeShares, [_goulash]);
      expect(
        await database.select(database.shoppingListItemRecipes).get(),
        hasLength(1),
      );
    });

    test('addItem at 1000 items in the list throws for the items per list and '
        'changes nothing', () async {
      await database.batch(
        (batch) => batch.insertAll(database.shoppingListItems, [
          for (var index = 3; index < 1000; index++)
            ShoppingListItemsCompanion.insert(
              id: 'item-$index',
              scopeId: _listId,
              name: 'Artikel $index',
              category: 'miscellaneous',
              language: 'de',
            ),
        ]),
      );

      await expectLater(
        addItem('Milch'),
        throwsA(
          isA<ShoppingListLimitExceededException>().having(
            (exception) => exception.limit,
            'limit',
            ShoppingListLimit.itemsPerList,
          ),
        ),
      );

      expect(await harness.readCommands(), isEmpty);
      expect((await latestList())!.items, hasLength(1000));
    });
  });
}
