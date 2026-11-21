import 'package:drift/drift.dart';

part 'app_database.g.dart';

@DataClassName('ShoppingList')
class ShoppingLists extends Table {
  @override
  String get tableName => 'lists';

  TextColumn get id => text()();
  TextColumn get name => text()();
  DateTimeColumn get createdAt => dateTime()();
  DateTimeColumn get updatedAt => dateTime()();

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
  DateTimeColumn get createdAt => dateTime()();

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
  BoolColumn get checked => boolean()();
  DateTimeColumn get createdAt => dateTime()();
  DateTimeColumn get updatedAt => dateTime()();

  @override
  Set<Column<Object>> get primaryKey => {id};
}

class PendingCommands extends Table {
  IntColumn get sequence => integer().autoIncrement()();
  TextColumn get id => text().unique()();
  TextColumn get type => text()();
  TextColumn get argumentsJson => text()();
  DateTimeColumn get occurredAt => dateTime()();
}

@DataClassName('SyncStateEntry')
class SyncState extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column<Object>> get primaryKey => {key};
}

@DriftDatabase(
  tables: [
    ShoppingLists,
    ShoppingListMembers,
    ShoppingListItems,
    PendingCommands,
    SyncState,
  ],
)
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  @override
  int get schemaVersion => 1;

  static const _syncTokenKey = 'token';

  Future<String?> readSyncToken() async =>
      (await (select(syncState)
                ..where((entry) => entry.key.equals(_syncTokenKey)))
              .getSingleOrNull())
          ?.value;

  Future<void> writeSyncToken(String? token) async {
    if (token == null) {
      await (delete(
        syncState,
      )..where((entry) => entry.key.equals(_syncTokenKey))).go();
      return;
    }
    await into(syncState).insertOnConflictUpdate(
      SyncStateCompanion.insert(key: _syncTokenKey, value: token),
    );
  }

  Future<void> deleteAllData() => transaction(() async {
    for (final table in allTables) {
      await delete(table).go();
    }
  });
}
