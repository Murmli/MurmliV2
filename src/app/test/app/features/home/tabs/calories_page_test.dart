import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/shopping_list.dart';
import 'package:app/app/features/shopping_list/providers/first_shopping_list_provider.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_repository.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../../helpers/pump_app.dart';

void main() {
  final translations = AppLocale.de.buildSync().home.tabs.calories;

  Future<ProviderContainer> openCaloriesTab(
    WidgetTester tester, {
    required bool debugBuild,
  }) async {
    final container = await pumpApp(
      tester,
      debugBuild: debugBuild,
      signedInAccount: const Account(id: 'account'),
    );
    container.read(firstShoppingListProvider);
    await tester.tap(find.text(translations.label));
    await tester.pumpAndSettle();
    return container;
  }

  Future<ShoppingList> currentList(
    WidgetTester tester,
    ProviderContainer container,
  ) async => (await tester.runAsync(
    () => container
        .read(shoppingListRepositoryProvider)
        .watchList()
        .firstWhere((list) => list != null),
  ))!;

  group('CaloriesPage', () {
    testWidgets('in a release build shows no button to reset the sample data', (
      tester,
    ) async {
      await openCaloriesTab(tester, debugBuild: false);

      expect(find.text(translations.resetSampleData), findsNothing);
    });

    testWidgets(
      'in a debug build tapping the reset button replaces the changed shopping list with the sample data',
      (tester) async {
        final container = await openCaloriesTab(tester, debugBuild: true);
        final sampleList = await currentList(tester, container);
        final repository = container.read(shoppingListRepositoryProvider);
        await tester.runAsync(() async {
          await repository.deleteAllItems(sampleList.id);
          await repository.addItem(
            listId: sampleList.id,
            name: 'Kaffee',
            category: MarketCategory.beverages,
          );
        });

        await tester.tap(find.text(translations.resetSampleData));
        await tester.pumpAndSettle();

        final resetList = await currentList(tester, container);
        expect(
          resetList.items.map((item) => item.name),
          sampleList.items.map((item) => item.name),
        );
      },
    );
  });
}
