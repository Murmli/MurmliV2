import 'package:drift/drift.dart';

import 'app_database.steps.dart';
import 'tables/shopping_list_tables.dart';
import 'tables/sync_tables.dart';

part 'app_database.g.dart';

@DriftDatabase(
  tables: [
    ShoppingLists,
    ShoppingListItems,
    ShoppingListItemRecipes,
    ShoppingCategoryPositions,
    SyncCommands,
    SyncStates,
    SyncScopes,
    SyncMembers,
    SyncNotices,
  ],
)
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  @override
  int get schemaVersion => 3;

  @override
  MigrationStrategy get migration => MigrationStrategy(
    onCreate: (migrator) => migrator.createAll(),
    onUpgrade: (migrator, from, to) async {
      if (from > to) {
        await _recreateAllTables(migrator);
        return;
      }
      await _runMigrationSteps(migrator, from, to);
      await _discardSyncToken();
    },
    beforeOpen: (details) => customStatement('PRAGMA foreign_keys = ON'),
  );

  Future<void> _runMigrationSteps(Migrator migrator, int from, int to) async {
    await customStatement('PRAGMA foreign_keys = OFF');
    await migrator.runMigrationSteps(
      from: from,
      to: to,
      steps: migrationSteps(
        from1To2: (migrator, schema) => transaction(() async {
          await migrator.createTable(schema.syncCommands);
          await migrator.createTable(schema.syncStates);
          await migrator.createTable(schema.syncScopes);
          await migrator.createTable(schema.syncMembers);
          await migrator.createTable(schema.syncNotices);
        }),
        from2To3: (migrator, schema) => transaction(
          () => migrator.addColumn(
            schema.shoppingListItems,
            schema.shoppingListItems.changedBy,
          ),
        ),
      ),
    );
    final foreignKeyViolations = await customSelect('PRAGMA foreign_key_check')
        .get();
    if (foreignKeyViolations.isNotEmpty) {
      throw StateError(
        'The migration from $from to $to broke '
        '${foreignKeyViolations.length} foreign keys',
      );
    }
    await customStatement('PRAGMA foreign_keys = ON');
  }

  // A downgrade only happens when switching to an older build. Tables of the
  // newer version are unknown here and must go too, or a later upgrade could
  // not create them again; the synced data comes back from the server.
  Future<void> _recreateAllTables(Migrator migrator) => transaction(() async {
    final tables = await customSelect(
      "SELECT name FROM sqlite_master WHERE type = 'table' "
      "AND name NOT LIKE 'sqlite_%'",
    ).get();
    for (final table in tables) {
      await migrator.deleteTable(table.read<String>('name'));
    }
    await migrator.createAll();
  });

  // The new version may know data types or fields the old token never
  // fetched, so the next round fetches the full state once.
  Future<void> _discardSyncToken() => update(syncStates).write(
    SyncStatesCompanion.custom(
      token: const Constant<String>(null),
      localStateGeneration: syncStates.localStateGeneration + const Constant(1),
    ),
  );
}
