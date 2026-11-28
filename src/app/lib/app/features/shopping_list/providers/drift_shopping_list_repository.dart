import 'package:drift/drift.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/database/app_database.dart';
import '../../../core/identifiers/new_uuid.dart';
import '../../../core/sync/sync_core.dart';
import '../../../core/sync/sync_data_type.dart';
import '../../../core/sync/sync_write_transaction.dart';
import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import '../models/recipe_share.dart';
import '../models/shopping_list.dart';
import '../models/shopping_list_item.dart';
import '../sync/shopping_list_item_data_type.dart';
import 'shopping_list_repository.dart';

const _maxItemsPerList = 1000;
const _itemType = ShoppingListItemDataType();

class DriftShoppingListRepository implements ShoppingListRepository {
  DriftShoppingListRepository(this._database, this._syncCore);

  final AppDatabase _database;
  final SyncCore _syncCore;

  $ShoppingListsTable get _lists => _database.shoppingLists;
  $ShoppingListItemsTable get _items => _database.shoppingListItems;
  $ShoppingListItemRecipesTable get _recipes =>
      _database.shoppingListItemRecipes;
  $ShoppingCategoryPositionsTable get _categoryPositions =>
      _database.shoppingCategoryPositions;

  @override
  Stream<ShoppingList?> watchList() =>
      _listQuery().watch().map(_shoppingListOf);

  JoinedSelectStatement<HasResultSet, dynamic> _listQuery() =>
      _database.select(_lists).join([
        leftOuterJoin(_items, _items.scopeId.equalsExp(_lists.id)),
        leftOuterJoin(_recipes, _recipes.itemId.equalsExp(_items.id)),
      ])..orderBy([
        OrderingTerm.asc(_lists.name),
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
    return ShoppingList(
      id: list.id,
      name: list.name,
      items: _itemsOf(rows.where((row) => row.readTable(_lists).id == list.id)),
    );
  }

  List<ShoppingListItem> _itemsOf(Iterable<TypedResult> rows) {
    final itemRows = <String, ShoppingListItemRow>{};
    final sharesByItemId = <String, List<RecipeShare>>{};
    for (final row in rows) {
      final item = row.readTableOrNull(_items);
      if (item == null) {
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
    return [
      for (final item in itemRows.values)
        _shoppingListItemOf(item, sharesByItemId[item.id]!),
    ];
  }

  ShoppingListItem _shoppingListItemOf(
    ShoppingListItemRow row,
    List<RecipeShare> recipeShares,
  ) => ShoppingListItem(
    id: row.id,
    listId: row.scopeId,
    name: row.name,
    quantity: row.quantity,
    unit: _unitOf(row),
    category: _categoryOf(row),
    checked: row.checked,
    createdAt: _dateTimeOf(row.createdAt),
    changedAt: _dateTimeOf(row.changedAt),
    recipeShares: recipeShares,
  );

  MeasurementUnit? _unitOf(ShoppingListItemRow row) => switch (row.unit) {
    final unitKey? => MeasurementUnit.fromKey(unitKey),
    null => null,
  };

  MarketCategory _categoryOf(ShoppingListItemRow row) =>
      MarketCategory.fromKey(row.category) ?? MarketCategory.miscellaneous;

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
  }) => _write((transaction) async {
    final count = _items.id.count();
    final itemCount =
        await (_database.selectOnly(_items)
              ..addColumns([count])
              ..where(_items.scopeId.equals(listId)))
            .map((row) => row.read(count)!)
            .getSingle();
    if (itemCount >= _maxItemsPerList) {
      throw const ShoppingListLimitExceededException(
        ShoppingListLimit.itemsPerList,
      );
    }
    final id = newUuid();
    await transaction.create(
      dataType: _itemType,
      scopeId: listId,
      recordId: id,
      fields: {
        'name': name,
        'quantity': quantity,
        'unit': unit?.key,
        'category': category.key,
        'checked': false,
        'language': LocaleSettings.currentLocale.languageCode,
      },
    );
    return id;
  });

  // Compared with the values shown, so an unknown category or unit from the
  // server is only overwritten when the user changed its group.
  @override
  Future<void> updateItem(
    String itemId, {
    required String name,
    required double? quantity,
    required MeasurementUnit? unit,
    required MarketCategory category,
  }) => _write((transaction) async {
    final row = await _itemRow(itemId);
    if (row == null) {
      return;
    }
    final fields = {
      if (name != row.name) 'name': name,
      if (quantity != row.quantity || unit != _unitOf(row)) ...{
        'quantity': quantity,
        'unit': unit?.key,
      },
      if (category != _categoryOf(row)) 'category': category.key,
    };
    if (fields.isNotEmpty) {
      await _setItemFields(transaction, row, fields);
    }
  });

  @override
  Future<void> setChecked(String itemId, {required bool checked}) =>
      _write((transaction) async {
        if (await _itemRow(itemId) case final row?) {
          await _setItemFields(transaction, row, {'checked': checked});
        }
      });

  @override
  Future<void> deleteItem(String itemId) => _write((transaction) async {
    if (await _itemRow(itemId) case final row?) {
      await _deleteItem(transaction, row);
    }
  });

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
  Future<void> deleteCheckedItems(String listId) => _write((transaction) async {
    final checkedItems = await (_itemsQuery(
      listId,
    )..where((item) => item.checked)).get();
    for (final row in checkedItems) {
      await _deleteItem(transaction, row);
    }
  });

  @override
  Future<void> deleteRecipeItems(String listId) => _write((transaction) async {
    final query = _database.select(_items).join([
      innerJoin(_recipes, _recipes.itemId.equalsExp(_items.id)),
    ])..where(_items.scopeId.equals(listId));
    for (final item in _itemsOf(await query.get())) {
      final ownQuantity = item.ownQuantity;
      final row = (await _itemRow(item.id))!;
      if (ownQuantity == null || ownQuantity == 0) {
        await _deleteItem(transaction, row);
        continue;
      }
      await _setItemFields(transaction, row, {
        'quantity': ownQuantity,
        'unit': row.unit,
      });
      await (_database.delete(
        _recipes,
      )..where((recipe) => recipe.itemId.equals(item.id))).go();
    }
  });

  @override
  Future<void> deleteAllItems(String listId) => _write((transaction) async {
    for (final row in await _itemsQuery(listId).get()) {
      await _deleteItem(transaction, row);
    }
  });

  SimpleSelectStatement<$ShoppingListItemsTable, ShoppingListItemRow>
  _itemsQuery(String listId) =>
      _database.select(_items)..where((item) => item.scopeId.equals(listId));

  Future<ShoppingListItemRow?> _itemRow(String itemId) => (_database.select(
    _items,
  )..where((item) => item.id.equals(itemId))).getSingleOrNull();

  Future<void> _setItemFields(
    SyncWriteTransaction transaction,
    ShoppingListItemRow row,
    Map<String, Object?> fields,
  ) => transaction.setFields(
    dataType: _itemType,
    scopeId: row.scopeId,
    recordId: row.id,
    fields: fields,
  );

  Future<void> _deleteItem(
    SyncWriteTransaction transaction,
    ShoppingListItemRow row,
  ) => transaction.delete(
    dataType: _itemType,
    scopeId: row.scopeId,
    recordId: row.id,
  );

  Future<T> _write<T>(
    Future<T> Function(SyncWriteTransaction transaction) action,
  ) async {
    try {
      return await _syncCore.write(action);
    } on SyncLimitExceededException catch (exception) {
      final limit = exception.dataType == _itemType.name
          ? switch (exception.field) {
              'name' => ShoppingListLimit.itemName,
              'quantity' => ShoppingListLimit.quantity,
              _ => null,
            }
          : null;
      if (limit == null) {
        rethrow;
      }
      throw ShoppingListLimitExceededException(limit);
    }
  }
}
