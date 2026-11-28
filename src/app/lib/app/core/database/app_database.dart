import 'package:drift/drift.dart';

import 'tables/shopping_list_tables.dart';

part 'app_database.g.dart';

@DriftDatabase(
  tables: [
    ShoppingLists,
    ShoppingListItems,
    ShoppingListItemRecipes,
    ShoppingCategoryPositions,
  ],
)
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  @override
  int get schemaVersion => 1;

  @override
  MigrationStrategy get migration => MigrationStrategy(
    beforeOpen: (details) => customStatement('PRAGMA foreign_keys = ON'),
  );
}
