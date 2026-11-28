import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/features/shopping_list/models/shopping_list.dart';
import 'package:app/app/features/shopping_list/providers/first_shopping_list_provider.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_repository.dart';

import '../../../../helpers/preferences.dart';
import '../../../../helpers/provider_container.dart';

void main() {
  const account = Account(id: 'account');

  Future<ProviderContainer> startApp({
    required bool debugBuild,
    Flavor flavor = Flavor.development,
    Account? signedInAccount = account,
    AppDatabase? database,
  }) async {
    final container = createContainer(
      await createPreferences(),
      debugBuild: debugBuild,
      flavor: flavor,
      signedInAccount: signedInAccount,
      database: database,
    );
    container.read(firstShoppingListProvider);
    await pumpEventQueue();
    return container;
  }

  Future<ShoppingList?> currentList(ProviderContainer container) =>
      container.read(shoppingListRepositoryProvider).watchList().first;

  group('FirstShoppingList', () {
    test('signed in in a debug build creates the list with sample data including the recipe shares of the sour cream', () async {
      final container = await startApp(debugBuild: true);

      final list = await currentList(container);
      expect(list?.name, '');
      expect(list?.items.length, greaterThanOrEqualTo(15));
      expect(list?.items.where((item) => item.checked), hasLength(2));
      final sourCream = list!.items.singleWhere(
        (item) => item.name == 'Sauerrahm',
      );
      expect(sourCream.quantity, 500);
      expect(
        sourCream.recipeShares.map(
          (share) => (share.recipeTitle, share.quantity),
        ),
        unorderedEquals([('Gulasch', 200.0), ('Ofenkartoffeln', 300.0)]),
      );
      final eggs = list.items.singleWhere((item) => item.name == 'Eier');
      expect(eggs.ownQuantity, 4);
    });

    test('signed in in a release build of the development flavor creates an empty list', () async {
      final container = await startApp(debugBuild: false);

      final list = await currentList(container);
      expect(list, isNotNull);
      expect(list?.items, isEmpty);
    });

    test('signed in in a release build of the local flavor creates the list with sample data', () async {
      final container = await startApp(debugBuild: false, flavor: Flavor.local);

      expect((await currentList(container))?.items, isNotEmpty);
    });

    test('second start with an existing list creates neither a list nor sample data', () async {
      final database = AppDatabase(NativeDatabase.memory());
      addTearDown(database.close);
      final firstStart = await startApp(debugBuild: true, database: database);
      final firstList = (await currentList(firstStart))!;
      await database.delete(database.shoppingListItems).go();
      firstStart.dispose();

      final secondStart = await startApp(debugBuild: true, database: database);

      final list = await currentList(secondStart);
      expect(list?.id, firstList.id);
      expect(list?.items, isEmpty);
      expect(await database.select(database.shoppingLists).get(), hasLength(1));
    });

    test('signed out creates no list', () async {
      final container = await startApp(debugBuild: true, signedInAccount: null);

      expect(await currentList(container), isNull);
    });
  });
}
