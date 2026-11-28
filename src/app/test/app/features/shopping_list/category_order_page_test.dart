import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/features/settings/settings_page.dart';
import 'package:app/app/features/shopping_list/category_order_page.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_overview_provider.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/pump_app.dart';
import '../../../helpers/shopping_list.dart';

void main() {
  final translations = AppLocale.de.buildSync();
  final shoppingList = translations.shoppingList;
  final categoryOrder = shoppingList.categoryOrder;
  final categories = shoppingList.categories;

  Future<ProviderContainer> openListMenu(
    WidgetTester tester, {
    required bool withSampleItems,
  }) async {
    final container = await pumpApp(
      tester,
      signedInAccount: const Account(id: 'account'),
      shoppingList: TestShoppingList(
        items: withSampleItems ? sampleShoppingListItems : const [],
      ),
    );
    await tester.tap(find.text(translations.home.tabs.shoppingList.label));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip(shoppingList.menu.tooltip));
    await tester.pumpAndSettle();
    return container;
  }

  Future<ProviderContainer> openCategoryOrder(
    WidgetTester tester, {
    required bool withSampleItems,
  }) async {
    final container = await openListMenu(
      tester,
      withSampleItems: withSampleItems,
    );
    await tester.tap(find.text(shoppingList.menu.sortCategories));
    await tester.pumpAndSettle();
    return container;
  }

  Finder tileOf(MarketCategory category) => find.descendant(
    of: find.byType(CategoryOrderPage),
    matching: find.byKey(ValueKey(category)),
  );

  double topOf(WidgetTester tester, Finder finder) =>
      tester.getTopLeft(finder).dy;

  List<MarketCategory> storedOrder(ProviderContainer container) =>
      container.read(marketCategoryOrderProvider).requireValue;

  Finder resetButton() => find.ancestor(
    of: find.text(categoryOrder.restoreDefault),
    matching: find.byWidgetPredicate((widget) => widget is TextButton),
  );

  Future<void> moveFruitsUp(WidgetTester tester) async {
    await tester.tap(
      find.byTooltip(categoryOrder.moveUp(name: categories.fruits)),
    );
    await tester.pumpAndSettle();
  }

  group('CategoryOrderPage', () {
    testWidgets('lists every category, empty ones too, in the default order', (
      tester,
    ) async {
      await openCategoryOrder(tester, withSampleItems: false);

      expect(
        topOf(tester, tileOf(MarketCategory.vegetables)),
        lessThan(topOf(tester, tileOf(MarketCategory.fruits))),
      );
      await tester.scrollUntilVisible(
        tileOf(MarketCategory.miscellaneous),
        200,
        scrollable: find.descendant(
          of: find.byType(CategoryOrderPage),
          matching: find.byType(Scrollable),
        ),
      );
      expect(tileOf(MarketCategory.miscellaneous), findsOneWidget);
    });

    testWidgets('moving a category down with its button stores the new order', (
      tester,
    ) async {
      final container = await openCategoryOrder(tester, withSampleItems: false);

      await tester.tap(
        find.byTooltip(categoryOrder.moveDown(name: categories.vegetables)),
      );
      await tester.pumpAndSettle();

      expect(
        topOf(tester, tileOf(MarketCategory.fruits)),
        lessThan(topOf(tester, tileOf(MarketCategory.vegetables))),
      );
      expect(storedOrder(container).take(2), [
        MarketCategory.fruits,
        MarketCategory.vegetables,
      ]);
    });

    testWidgets('dragging a category by its handle stores the new order', (
      tester,
    ) async {
      final container = await openCategoryOrder(tester, withSampleItems: false);

      final gesture = await tester.startGesture(
        tester.getCenter(
          find.descendant(
            of: tileOf(MarketCategory.vegetables),
            matching: find.byIcon(Icons.drag_indicator),
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));
      await gesture.moveBy(const Offset(0, 40));
      await tester.pump();
      await gesture.moveBy(const Offset(0, 40));
      await tester.pump();
      await gesture.up();
      await tester.pumpAndSettle();

      expect(storedOrder(container).first, isNot(MarketCategory.vegetables));
    });

    testWidgets('a changed order sorts the groups of the shopping list', (
      tester,
    ) async {
      await openCategoryOrder(tester, withSampleItems: true);

      await moveFruitsUp(tester);
      await tester.tap(
        find.byTooltip(
          MaterialLocalizations.of(
            tester.element(find.byType(CategoryOrderPage)),
          ).backButtonTooltip,
        ),
      );
      await tester.pumpAndSettle();

      expect(
        topOf(tester, find.text(categories.fruits)),
        lessThan(topOf(tester, find.text(categories.vegetables))),
      );
    });

    testWidgets('reset is disabled while the default order applies', (
      tester,
    ) async {
      await openCategoryOrder(tester, withSampleItems: false);

      expect(tester.widget<TextButton>(resetButton()).onPressed, isNull);
    });

    testWidgets('cancelling the reset keeps the changed order', (tester) async {
      final container = await openCategoryOrder(tester, withSampleItems: false);
      await moveFruitsUp(tester);

      await tester.tap(resetButton());
      await tester.pumpAndSettle();
      await tester.tap(find.text(categoryOrder.resetDialog.cancel));
      await tester.pumpAndSettle();

      expect(
        topOf(tester, tileOf(MarketCategory.fruits)),
        lessThan(topOf(tester, tileOf(MarketCategory.vegetables))),
      );
      expect(storedOrder(container).first, MarketCategory.fruits);
    });

    testWidgets('confirming the reset restores the default order', (
      tester,
    ) async {
      final container = await openCategoryOrder(tester, withSampleItems: false);
      await moveFruitsUp(tester);

      await tester.tap(resetButton());
      await tester.pumpAndSettle();
      await tester.tap(
        find.widgetWithText(FilledButton, categoryOrder.resetDialog.confirm),
      );
      await tester.pumpAndSettle();

      expect(
        topOf(tester, tileOf(MarketCategory.vegetables)),
        lessThan(topOf(tester, tileOf(MarketCategory.fruits))),
      );
      expect(storedOrder(container), MarketCategory.values);
    });
  });

  group('showShoppingListMenu', () {
    testWidgets('the settings entry opens the settings', (tester) async {
      await openListMenu(tester, withSampleItems: false);

      await tester.tap(find.text(translations.settings.title));
      await tester.pumpAndSettle();

      expect(find.byType(SettingsPage), findsOneWidget);
    });
  });
}
