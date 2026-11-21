import 'package:drift/drift.dart';

part 'app_database.g.dart';

@DataClassName('ShoppingList')
class ShoppingLists extends Table {
  @override
  String get tableName => 'lists';

  TextColumn get id => text()();
  TextColumn get name => text()();
  TextColumn get createdAt => text()();
  TextColumn get updatedAt => text()();

  @override
  Set<Column<Object>> get primaryKey => {id};
}

@DataClassName('ShoppingListMember')
class ShoppingListMembers extends Table {
  @override
  String get tableName => 'list_members';

  TextColumn get id => text()();
  TextColumn get listId => text()();
  TextColumn get userId => text()();
  TextColumn get createdAt => text()();

  @override
  Set<Column<Object>> get primaryKey => {id};
}

@DataClassName('ShoppingListItem')
class ShoppingListItems extends Table {
  @override
  String get tableName => 'items';

  TextColumn get id => text()();
  TextColumn get listId => text()();
  TextColumn get name => text()();
  RealColumn get quantity => real().nullable()();
  TextColumn get unit => text().nullable()();
  TextColumn get category => text().nullable()();
  IntColumn get checked => integer()();
  TextColumn get createdAt => text()();
  TextColumn get updatedAt => text()();

  @override
  Set<Column<Object>> get primaryKey => {id};
}

@DriftDatabase(tables: [ShoppingLists, ShoppingListMembers, ShoppingListItems])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  @override
  int get schemaVersion => 1;

  // PowerSync creates the tables as views from its schema, drift must not.
  @override
  MigrationStrategy get migration =>
      MigrationStrategy(onCreate: (migrator) async {});
}
