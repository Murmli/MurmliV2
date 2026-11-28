// dart format width=80
// ignore_for_file: unused_local_variable, unused_import
import 'package:drift/drift.dart';
import 'package:drift_dev/api/migrations_native.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:flutter_test/flutter_test.dart';

import 'generated/schema.dart';

import 'generated/schema_v1.dart' as v1;
import 'generated/schema_v2.dart' as v2;
import 'generated/schema_v3.dart' as v3;

void main() {
  driftRuntimeOptions.dontWarnAboutMultipleDatabases = true;
  late SchemaVerifier verifier;

  setUpAll(() {
    verifier = SchemaVerifier(GeneratedHelper());
  });

  group('simple database migrations', () {
    // These simple tests verify all possible schema updates with a simple (no
    // data) migration. This is a quick way to ensure that written database
    // migrations properly alter the schema.
    const versions = GeneratedHelper.versions;
    for (final (i, fromVersion) in versions.indexed) {
      group('from $fromVersion', () {
        for (final toVersion in versions.skip(i + 1)) {
          test('to $toVersion', () async {
            final schema = await verifier.schemaAt(fromVersion);
            final db = AppDatabase(schema.newConnection());
            await verifier.migrateAndValidate(db, toVersion);
            await db.close();
          });
        }
      });
    }
  });

  test('migration from v1 to v2 keeps the shopping list and adds empty sync tables', () async {
    const oldList = v1.ShoppingListsData(id: 'list', name: 'Groceries');
    const oldItem = v1.ShoppingListItemsData(
      id: 'item',
      scopeId: 'list',
      name: 'Milk',
      quantity: 2,
      unit: 'l',
      category: 'dairy',
      checked: 1,
      language: 'de',
      createdAt: 1,
      changedAt: 2,
    );
    const oldRecipe = v1.ShoppingListItemRecipesData(
      itemId: 'item',
      recipeId: 'recipe',
      recipeTitle: 'Pancakes',
      quantity: 0.5,
    );
    const oldPosition = v1.ShoppingCategoryPositionsData(
      category: 'dairy',
      position: 3,
    );

    await verifier.testWithDataIntegrity(
      oldVersion: 1,
      newVersion: 2,
      createOld: v1.DatabaseAtV1.new,
      createNew: v2.DatabaseAtV2.new,
      openTestedDatabase: AppDatabase.new,
      createItems: (batch, oldDb) {
        batch
          ..insert(oldDb.shoppingLists, oldList)
          ..insert(oldDb.shoppingListItems, oldItem)
          ..insert(oldDb.shoppingListItemRecipes, oldRecipe)
          ..insert(oldDb.shoppingCategoryPositions, oldPosition);
      },
      validateItems: (newDb) async {
        expect(await newDb.select(newDb.shoppingLists).get(), [
          v2.ShoppingListsData.fromJson(oldList.toJson()),
        ]);
        expect(await newDb.select(newDb.shoppingListItems).get(), [
          v2.ShoppingListItemsData.fromJson(oldItem.toJson()),
        ]);
        expect(await newDb.select(newDb.shoppingListItemRecipes).get(), [
          v2.ShoppingListItemRecipesData.fromJson(oldRecipe.toJson()),
        ]);
        expect(await newDb.select(newDb.shoppingCategoryPositions).get(), [
          v2.ShoppingCategoryPositionsData.fromJson(oldPosition.toJson()),
        ]);
        expect(await newDb.select(newDb.syncCommands).get(), isEmpty);
        expect(await newDb.select(newDb.syncStates).get(), isEmpty);
        expect(await newDb.select(newDb.syncScopes).get(), isEmpty);
        expect(await newDb.select(newDb.syncMembers).get(), isEmpty);
        expect(await newDb.select(newDb.syncNotices).get(), isEmpty);
      },
    );
  });

  test(
    'migration from v2 to v3 keeps every row with an empty changedBy',
    () async {
      const oldList = v2.ShoppingListsData(id: 'list', name: 'Groceries');
      const oldItem = v2.ShoppingListItemsData(
        id: 'item',
        scopeId: 'list',
        name: 'Milk',
        quantity: 2,
        unit: 'liter',
        category: 'dairy',
        checked: 1,
        language: 'de',
        createdAt: 1,
        changedAt: 2,
      );
      const oldRecipe = v2.ShoppingListItemRecipesData(
        itemId: 'item',
        recipeId: 'recipe',
        recipeTitle: 'Pancakes',
        quantity: 0.5,
      );
      const oldPosition = v2.ShoppingCategoryPositionsData(
        category: 'dairy',
        position: 3,
      );
      const oldCommand = v2.SyncCommandsData(
        sequence: 1,
        id: 'command',
        kind: 'setFields',
        scopeId: 'list',
        dataType: 'shoppingListItem',
        recordId: 'item',
        fields: '{"checked":true}',
        time: 3,
        failedAttempts: 0,
      );

      await verifier.testWithDataIntegrity(
        oldVersion: 2,
        newVersion: 3,
        createOld: v2.DatabaseAtV2.new,
        createNew: v3.DatabaseAtV3.new,
        openTestedDatabase: AppDatabase.new,
        createItems: (batch, oldDb) {
          batch
            ..insert(oldDb.shoppingLists, oldList)
            ..insert(oldDb.shoppingListItems, oldItem)
            ..insert(oldDb.shoppingListItemRecipes, oldRecipe)
            ..insert(oldDb.shoppingCategoryPositions, oldPosition)
            ..insert(oldDb.syncCommands, oldCommand);
        },
        validateItems: (newDb) async {
          expect(await newDb.select(newDb.shoppingLists).get(), [
            v3.ShoppingListsData.fromJson(oldList.toJson()),
          ]);
          expect(await newDb.select(newDb.shoppingListItems).get(), [
            v3.ShoppingListItemsData.fromJson(oldItem.toJson()),
          ]);
          expect(await newDb.select(newDb.shoppingListItemRecipes).get(), [
            v3.ShoppingListItemRecipesData.fromJson(oldRecipe.toJson()),
          ]);
          expect(await newDb.select(newDb.shoppingCategoryPositions).get(), [
            v3.ShoppingCategoryPositionsData.fromJson(oldPosition.toJson()),
          ]);
          expect(await newDb.select(newDb.syncCommands).get(), [
            v3.SyncCommandsData.fromJson(oldCommand.toJson()),
          ]);
        },
      );
    },
  );
}
