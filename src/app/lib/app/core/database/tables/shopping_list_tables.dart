import 'package:drift/drift.dart';

@DataClassName('ShoppingListRow')
class ShoppingLists extends Table {
  TextColumn get id => text()();
  TextColumn get name => text()();

  @override
  Set<Column<Object>> get primaryKey => {id};
}

@DataClassName('ShoppingListItemRow')
@TableIndex(name: 'shopping_list_items_scope_id', columns: {#scopeId})
class ShoppingListItems extends Table {
  TextColumn get id => text()();
  TextColumn get scopeId => text()();
  TextColumn get name => text()();
  RealColumn get quantity => real().nullable()();
  TextColumn get unit => text().nullable()();
  TextColumn get category => text()();
  BoolColumn get checked => boolean().withDefault(const Constant(false))();
  TextColumn get language => text()();
  IntColumn get createdAt => integer().nullable()();
  IntColumn get changedAt => integer().nullable()();
  TextColumn get changedBy => text().nullable()();

  @override
  Set<Column<Object>> get primaryKey => {id};
}

@DataClassName('ShoppingListItemRecipeRow')
class ShoppingListItemRecipes extends Table {
  TextColumn get itemId =>
      text().references(ShoppingListItems, #id, onDelete: KeyAction.cascade)();
  TextColumn get recipeId => text()();
  TextColumn get recipeTitle => text()();
  RealColumn get quantity => real().nullable()();

  @override
  Set<Column<Object>> get primaryKey => {itemId, recipeId};
}

@DataClassName('ShoppingCategoryPositionRow')
class ShoppingCategoryPositions extends Table {
  TextColumn get category => text()();
  IntColumn get position => integer()();

  @override
  Set<Column<Object>> get primaryKey => {category};
}
