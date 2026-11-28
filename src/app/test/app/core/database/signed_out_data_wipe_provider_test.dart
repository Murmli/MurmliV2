import 'package:drift/drift.dart' show Value;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/database/signed_out_data_wipe_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/providers/first_shopping_list_provider.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_repository.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';

void main() {
  late AppDatabase database;

  Future<ProviderContainer> startApp() async {
    final container = createContainer(
      await createPreferences({}),
      flavor: Flavor.development,
      debugBuild: true,
      database: database,
    );
    container
      ..read(signedOutDataWipeProvider)
      ..read(firstShoppingListProvider);
    await pumpEventQueue();
    return container;
  }

  Future<Map<String, int>> rowCountsByTable() async => {
    for (final table in database.allTables)
      table.actualTableName: (await database.select(table).get()).length,
  };

  Future<void> insertSyncRows() async {
    await database
        .into(database.syncCommands)
        .insert(
          SyncCommandsCompanion.insert(
            id: 'command',
            kind: 'upsert',
            scopeId: 'scope',
            dataType: 'shoppingListItem',
            recordId: 'record',
            time: 1,
          ),
        );
    await database
        .into(database.syncStates)
        .insert(SyncStatesCompanion.insert(id: const Value(1)));
    await database
        .into(database.syncScopes)
        .insert(
          SyncScopesCompanion.insert(scopeId: 'scope', kind: 'shoppingList'),
        );
    await database
        .into(database.syncMembers)
        .insert(
          SyncMembersCompanion.insert(
            scopeId: 'scope',
            memberId: 'member',
            name: 'Member',
            role: 'owner',
            state: 'active',
            isOwn: true,
          ),
        );
    await database
        .into(database.syncNotices)
        .insert(
          SyncNoticesCompanion.insert(
            kind: 'removedScope',
            scopeId: 'scope',
            createdAt: 1,
          ),
        );
  }

  setUp(() {
    database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);
  });

  group('SignedOutDataWipe', () {
    test(
      'sign-out empties all tables and the next sign-in creates a new list',
      () async {
        final container = await startApp();
        await signIn(container);
        await pumpEventQueue();
        final repository = container.read(shoppingListRepositoryProvider);
        final firstList = (await repository.watchList().first)!;
        await repository.setCategoryOrder([MarketCategory.frozen]);
        await insertSyncRows();

        await container.read(signedInAccountProvider.notifier).signOut();
        await pumpEventQueue();
        final rowCountsAfterSignOut = await rowCountsByTable();
        await signIn(container);
        await pumpEventQueue();

        expect(rowCountsAfterSignOut.values, everyElement(0));
        final newList = await repository.watchList().first;
        expect(newList, isNotNull);
        expect(newList?.id, isNot(firstList.id));
      },
    );

    test('start without sign-in empties all tables', () async {
      final previousSession = await startApp();
      await signIn(previousSession);
      await pumpEventQueue();
      await previousSession
          .read(shoppingListRepositoryProvider)
          .setCategoryOrder([MarketCategory.frozen]);
      await insertSyncRows();
      previousSession.dispose();
      expect((await rowCountsByTable()).values, everyElement(isPositive));

      await startApp();

      expect((await rowCountsByTable()).values, everyElement(0));
    });
  });
}
