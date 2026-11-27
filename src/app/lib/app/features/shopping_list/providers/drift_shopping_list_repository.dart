import 'package:clock/clock.dart';
import 'package:drift/drift.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/database/app_database.dart';
import '../../../core/identifiers/new_uuid.dart';
import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import '../models/recipe_share.dart';
import '../models/shopping_list.dart';
import '../models/shopping_list_item.dart';
import 'shopping_list_repository.dart';

class DriftShoppingListRepository implements ShoppingListRepository {
  DriftShoppingListRepository(this._database, this._clock);

  final AppDatabase _database;
  final Clock _clock;

  $ShoppingListsTable get _lists => _database.shoppingLists;
  $ShoppingListItemsTable get _items => _database.shoppingListItems;
  $ShoppingListItemRecipesTable get _recipes =>
      _database.shoppingListItemRecipes;
  $ShoppingCategoryPositionsTable get _categoryPositions =>
      _database.shoppingCategoryPositions;

  int get _nowInMilliseconds => _clock.now().millisecondsSinceEpoch;

  @override
  Stream<ShoppingList?> watchList() =>
      _listQuery().watch().map(_shoppingListOf);

  JoinedSelectStatement<HasResultSet, dynamic> _listQuery() =>
      _database.select(_lists).join([
        leftOuterJoin(_items, _items.scopeId.equalsExp(_lists.id)),
        leftOuterJoin(_recipes, _recipes.itemId.equalsExp(_items.id)),
      ])..orderBy([
        OrderingTerm.asc(_lists.id),
        OrderingTerm.asc(_items.createdAt),
        OrderingTerm.asc(_items.id),
        OrderingTerm.asc(_recipes.recipeId),
      ]);

  ShoppingList? _shoppingListOf(List<TypedResult> rows) {
    if (rows.isEmpty) {
      return null;
    }
    final list = rows.first.readTable(_lists);
    final itemRows = <String, ShoppingListItemRow>{};
    final sharesByItemId = <String, List<RecipeShare>>{};
    for (final row in rows) {
      final item = row.readTableOrNull(_items);
      if (row.readTable(_lists).id != list.id || item == null) {
        continue;
      }
      itemRows[item.id] = item;
      final shares = sharesByItemId.putIfAbsent(item.id, () => []);
      if (row.readTableOrNull(_recipes) case final recipe?) {
        shares.add(
          RecipeShare(
            recipeId: recipe.recipeId,
            recipeTitle: recipe.recipeTitle,
            quantity: recipe.quantity,
          ),
        );
      }
    }
    return ShoppingList(
      id: list.id,
      name: list.name,
      items: [
        for (final item in itemRows.values)
          _shoppingListItemOf(item, sharesByItemId[item.id]!),
      ],
    );
  }

  ShoppingListItem _shoppingListItemOf(
    ShoppingListItemRow row,
    List<RecipeShare> recipeShares,
  ) => ShoppingListItem(
    id: row.id,
    listId: row.scopeId,
    name: row.name,
    quantity: row.quantity,
    unit: switch (row.unit) {
      final unitKey? => MeasurementUnit.fromKey(unitKey),
      null => null,
    },
    category:
        MarketCategory.fromKey(row.category) ?? MarketCategory.miscellaneous,
    checked: row.checked,
    createdAt: _dateTimeOf(row.createdAt),
    changedAt: _dateTimeOf(row.changedAt),
    recipeShares: recipeShares,
  );

  DateTime? _dateTimeOf(int? milliseconds) => switch (milliseconds) {
    final milliseconds? => DateTime.fromMillisecondsSinceEpoch(
      milliseconds,
      isUtc: true,
    ),
    null => null,
  };

  @override
  Stream<List<MarketCategory>> watchCategoryOrder() {
    final query = _database.select(_categoryPositions)
      ..orderBy([(position) => OrderingTerm.asc(position.position)]);
    return query.watch().map((positions) {
      final storedCategories = {
        for (final position in positions)
          ?MarketCategory.fromKey(position.category),
      };
      return [
        ...storedCategories,
        ...MarketCategory.values.where(
          (category) => !storedCategories.contains(category),
        ),
      ];
    });
  }

  @override
  Future<String> addItem({
    required String listId,
    required String name,
    required MarketCategory category,
    double? quantity,
    MeasurementUnit? unit,
  }) => _database.transaction(() async {
    final id = newUuid();
    final now = _nowInMilliseconds;
    await _database
        .into(_items)
        .insert(
          ShoppingListItemsCompanion.insert(
            id: id,
            scopeId: listId,
            name: name,
            quantity: Value(quantity),
            unit: Value(unit?.key),
            category: category.key,
            language: LocaleSettings.currentLocale.languageCode,
            createdAt: Value(now),
            changedAt: Value(now),
          ),
        );
    return id;
  });

  @override
  Future<void> updateItem(
    String itemId, {
    required String name,
    required double? quantity,
    required MeasurementUnit? unit,
    required MarketCategory category,
  }) => _updateItem(
    itemId,
    ShoppingListItemsCompanion(
      name: Value(name),
      quantity: Value(quantity),
      unit: Value(unit?.key),
      category: Value(category.key),
    ),
  );

  @override
  Future<void> setChecked(String itemId, {required bool checked}) =>
      _updateItem(itemId, ShoppingListItemsCompanion(checked: Value(checked)));

  Future<void> _updateItem(String itemId, ShoppingListItemsCompanion changes) =>
      _database.transaction(
        () =>
            (_database.update(_items)..where((item) => item.id.equals(itemId)))
                .write(changes.copyWith(changedAt: Value(_nowInMilliseconds))),
      );

  @override
  Future<void> deleteItem(String itemId) => _database.transaction(
    () => (_database.delete(
      _items,
    )..where((item) => item.id.equals(itemId))).go(),
  );

  @override
  Future<void> setCategoryOrder(List<MarketCategory> categories) =>
      _database.transaction(() async {
        await _database.delete(_categoryPositions).go();
        await _database.batch(
          (batch) => batch.insertAll(_categoryPositions, [
            for (final (index, category) in categories.indexed)
              ShoppingCategoryPositionsCompanion.insert(
                category: category.key,
                position: index,
              ),
          ]),
        );
      });

  @override
  Future<void> resetCategoryOrder() =>
      _database.transaction(() => _database.delete(_categoryPositions).go());

  @override
  Future<void> deleteCheckedItems(String listId) => _database.transaction(
    () => (_database.delete(
      _items,
    )..where((item) => item.scopeId.equals(listId) & item.checked)).go(),
  );

  @override
  Future<void> deleteRecipeItems(String listId) =>
      _database.transaction(() async {
        final list = _shoppingListOf(await _listQuery().get());
        if (list == null || list.id != listId) {
          return;
        }
        final now = _nowInMilliseconds;
        for (final item in list.items) {
          if (item.recipeShares.isEmpty) {
            continue;
          }
          final ownQuantity = item.ownQuantity;
          if (ownQuantity == null || ownQuantity == 0) {
            await (_database.delete(
              _items,
            )..where((row) => row.id.equals(item.id))).go();
            continue;
          }
          await (_database.update(
            _items,
          )..where((row) => row.id.equals(item.id))).write(
            ShoppingListItemsCompanion(
              quantity: Value(ownQuantity),
              changedAt: Value(now),
            ),
          );
          await (_database.delete(
            _recipes,
          )..where((recipe) => recipe.itemId.equals(item.id))).go();
        }
      });

  @override
  Future<void> deleteAllItems(String listId) => _database.transaction(
    () => (_database.delete(
      _items,
    )..where((item) => item.scopeId.equals(listId))).go(),
  );
}
