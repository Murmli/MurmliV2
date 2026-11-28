import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/database/app_database_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/shopping_list.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_repository.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../../helpers/pump_app.dart';
import '../../../../helpers/shopping_list.dart';

void main() {
  final translations = AppLocale.de.buildSync().home.tabs.calories;

  Future<ProviderContainer> openCaloriesTab(
    WidgetTester tester, {
    required bool debugBuild,
    Flavor flavor = Flavor.development,
    TestShoppingList? shoppingList = const TestShoppingList(),
  }) async {
    final container = await pumpApp(
      tester,
      debugBuild: debugBuild,
      flavor: flavor,
      signedInAccount: const Account(id: 'account'),
      shoppingList: shoppingList,
    );
    await tester.tap(find.text(translations.label));
    await tester.pumpAndSettle();
    return container;
  }

  Finder addSampleItemsButton() => find.ancestor(
    of: find.text(translations.addSampleItems),
    matching: find.byType(OutlinedButton),
  );

  Future<ShoppingList> currentList(
    WidgetTester tester,
    ProviderContainer container,
  ) async => (await tester.runAsync(
    () => container.read(shoppingListRepositoryProvider).watchList().first,
  ))!;

  group('CaloriesPage', () {
    testWidgets(
      'in a release build of the development flavor shows no button to add sample items',
      (tester) async {
        await openCaloriesTab(tester, debugBuild: false);

        expect(find.text(translations.addSampleItems), findsNothing);
      },
    );

    testWidgets(
      'in a release build of the local flavor shows the button to add sample items',
      (tester) async {
        await openCaloriesTab(tester, debugBuild: false, flavor: Flavor.local);

        expect(find.text(translations.addSampleItems), findsOneWidget);
      },
    );

    testWidgets(
      'in a debug build tapping the button adds the sample items to the list and queues them for the server',
      (tester) async {
        final container = await openCaloriesTab(
          tester,
          debugBuild: true,
          shoppingList: const TestShoppingList(
            items: [TestShoppingListItem('Kaffee', MarketCategory.beverages)],
          ),
        );

        await tester.tap(addSampleItemsButton());
        await tester.pumpAndSettle();

        final list = await currentList(tester, container);
        expect(list.items.first.name, 'Kaffee');
        expect(
          list.items.map((item) => item.name),
          containsAll(['Paprika', 'Sauerrahm', 'Spülmittel']),
        );
        expect(list.items, hasLength(17));
        expect(
          list.items.where((item) => item.checked).map((item) => item.name),
          unorderedEquals(['Bananen', 'Milch']),
        );
        final database = container.read(appDatabaseProvider);
        final commands = (await tester.runAsync(
          () => database.select(database.syncCommands).get(),
        ))!;
        expect(
          commands.where((command) => command.kind == 'create'),
          hasLength(16),
        );
      },
    );

    testWidgets('without a list the button is disabled', (tester) async {
      await openCaloriesTab(tester, debugBuild: true, shoppingList: null);

      expect(
        tester.widget<OutlinedButton>(addSampleItemsButton()).onPressed,
        isNull,
      );
    });
  });
}
