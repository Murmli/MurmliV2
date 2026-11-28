import 'package:clock/clock.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/core/storage/shared_preferences_provider.dart';
import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/measurement_unit.dart';
import 'package:app/app/features/shopping_list/models/shopping_list.dart';
import 'package:app/app/features/shopping_list/models/shopping_list_item.dart';
import 'package:app/app/features/shopping_list/providers/first_shopping_list_provider.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_overview_provider.dart';
import 'package:app/app/features/shopping_list/providers/shopping_list_repository.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_checked_section.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_group_card.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_item_row.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../../helpers/pump_app.dart';

void main() {
  final translations = AppLocale.de.buildSync();
  final shoppingList = translations.shoppingList;
  final categories = shoppingList.categories;

  Future<ProviderContainer> openShoppingListTab(
    WidgetTester tester, {
    required bool debugBuild,
    Map<String, Object> storedPreferences = const {},
    Clock? clock,
  }) async {
    final container = await pumpApp(
      tester,
      debugBuild: debugBuild,
      storedPreferences: storedPreferences,
      clock: clock,
      signedInAccount: const Account(id: 'account'),
    );
    container.read(firstShoppingListProvider);
    await tester.tap(find.text(translations.home.tabs.shoppingList.label));
    await tester.pumpAndSettle();
    return container;
  }

  // Each write gets its own runAsync: drift re-runs the page's queries in the
  // fake zone, and a further write in the same runAsync would wait for them.
  Future<T> write<T>(
    WidgetTester tester,
    ProviderContainer container,
    Future<T> Function(ShoppingListRepository repository, ShoppingList list)
    operation,
  ) async {
    final list = container.read(currentShoppingListProvider).requireValue!;
    final result = await tester.runAsync(
      () => operation(container.read(shoppingListRepositoryProvider), list),
    );
    await tester.pumpAndSettle();
    return result as T;
  }

  Future<String> addItem(
    WidgetTester tester,
    ProviderContainer container,
    String name,
    MarketCategory category, {
    double? quantity,
    MeasurementUnit? unit,
  }) => write(
    tester,
    container,
    (repository, list) => repository.addItem(
      listId: list.id,
      name: name,
      category: category,
      quantity: quantity,
      unit: unit,
    ),
  );

  Future<void> check(
    WidgetTester tester,
    ProviderContainer container,
    String itemId,
  ) => write(
    tester,
    container,
    (repository, list) => repository.setChecked(itemId, checked: true),
  );

  Future<void> addMixedItems(
    WidgetTester tester,
    ProviderContainer container,
  ) async {
    await addItem(tester, container, 'Joghurt', MarketCategory.dairy);
    await addItem(tester, container, 'Karotten', MarketCategory.vegetables);
    await addItem(tester, container, 'Saft', MarketCategory.beverages);
    await check(
      tester,
      container,
      await addItem(tester, container, 'Tiefkühlpizza', MarketCategory.frozen),
    );
  }

  double topOf(WidgetTester tester, String text) =>
      tester.getTopLeft(find.text(text)).dy;

  Finder rowOf(String itemName) => find.ancestor(
    of: find.text(itemName),
    matching: find.byType(ShoppingListItemRow),
  );

  Finder inCheckedSection(String itemName) => find.descendant(
    of: find.byType(ShoppingListCheckedSection),
    matching: find.text(itemName),
  );

  ShoppingListItem storedItem(ProviderContainer container, String itemId) =>
      container
          .read(currentShoppingListProvider)
          .requireValue!
          .items
          .singleWhere((item) => item.id == itemId);

  group('ShoppingListPage', () {
    testWidgets(
      'groups the open items by category in the default order and lists checked items in the checked section',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);

        await addMixedItems(tester, container);

        expect(find.text(categories.frozen), findsNothing);
        expect(inCheckedSection('Tiefkühlpizza'), findsOneWidget);
        expect(
          topOf(tester, categories.vegetables),
          lessThan(topOf(tester, categories.dairy)),
        );
        expect(
          topOf(tester, categories.dairy),
          lessThan(topOf(tester, categories.beverages)),
        );
      },
    );

    testWidgets('follows a changed category order while the page is open', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      await addMixedItems(tester, container);

      await write(
        tester,
        container,
        (repository, list) => repository.setCategoryOrder([
          MarketCategory.beverages,
          ...MarketCategory.values.where(
            (category) => category != MarketCategory.beverages,
          ),
        ]),
      );

      expect(
        topOf(tester, categories.beverages),
        lessThan(topOf(tester, categories.vegetables)),
      );
    });

    testWidgets('shows quantity and unit of each item', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);

      await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
        quantity: 1.5,
        unit: MeasurementUnit.liter,
      );
      await addItem(
        tester,
        container,
        'Mehl',
        MarketCategory.bakingAndCooking,
        quantity: 500,
        unit: MeasurementUnit.gram,
      );
      await addItem(
        tester,
        container,
        'Zitronen',
        MarketCategory.fruits,
        quantity: 3,
      );
      await addItem(tester, container, 'Salz', MarketCategory.spices);

      expect(find.text('1,5 l'), findsOneWidget);
      expect(find.text('500 g'), findsOneWidget);
      expect(find.text('3'), findsOneWidget);
      expect(
        find.descendant(of: rowOf('Salz'), matching: find.byType(Text)),
        findsOneWidget,
      );
    });

    testWidgets(
      'shows the recipe of an item with one recipe and the recipe count of an item with several',
      (tester) async {
        await openShoppingListTab(tester, debugBuild: true);

        expect(
          find.descendant(of: rowOf('Paprika'), matching: find.text('Gulasch')),
          findsOneWidget,
        );
        expect(
          find.descendant(
            of: rowOf('Sauerrahm'),
            matching: find.text(shoppingList.recipeCount(count: 2)),
          ),
          findsOneWidget,
        );
      },
    );

    testWidgets('without items shows the empty state', (tester) async {
      await openShoppingListTab(tester, debugBuild: false);

      expect(find.text(shoppingList.empty.title), findsOneWidget);
      expect(find.byType(ShoppingListGroupCard), findsNothing);
    });

    testWidgets(
      'with only checked items shows the all checked hint instead of the empty state',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);

        await check(
          tester,
          container,
          await addItem(tester, container, 'Kaffee', MarketCategory.beverages),
        );

        expect(find.text(shoppingList.allChecked.title), findsOneWidget);
        expect(find.text(shoppingList.empty.title), findsNothing);
        expect(inCheckedSection('Kaffee'), findsOneWidget);
      },
    );

    testWidgets(
      'tapping the tile of an open item checks it and moves it to the checked section',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Butter',
          MarketCategory.dairy,
        );
        await addItem(tester, container, 'Brot', MarketCategory.bakery);

        await tester.tap(
          find.bySemanticsLabel(shoppingList.checkItem(name: 'Butter')),
        );
        await tester.pumpAndSettle();

        expect(inCheckedSection('Butter'), findsOneWidget);
        expect(
          find.text(shoppingList.checkedSection(count: 1)),
          findsOneWidget,
        );
        expect(
          find.text(shoppingList.progress(checked: 1, total: 2)),
          findsOneWidget,
        );
        expect(storedItem(container, itemId).checked, isTrue);
      },
    );

    testWidgets(
      'tapping the tile of a checked item reopens it in its category group',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        await addItem(tester, container, 'Brot', MarketCategory.bakery);
        final itemId = await addItem(
          tester,
          container,
          'Butter',
          MarketCategory.dairy,
        );
        await check(tester, container, itemId);

        await tester.tap(
          find.bySemanticsLabel(shoppingList.uncheckItem(name: 'Butter')),
        );
        await tester.pumpAndSettle();

        expect(
          find.descendant(
            of: find.byType(ShoppingListGroupCard),
            matching: find.text('Butter'),
          ),
          findsOneWidget,
        );
        expect(find.byType(ShoppingListCheckedSection), findsNothing);
        expect(storedItem(container, itemId).checked, isFalse);
      },
    );

    testWidgets(
      'checking an item through the screen reader stores it as checked',
      (tester) async {
        final semantics = tester.ensureSemantics();
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Butter',
          MarketCategory.dairy,
        );

        tester.semantics.tap(
          find.semantics.byLabel(shoppingList.checkItem(name: 'Butter')),
        );
        await tester.pumpAndSettle();

        expect(storedItem(container, itemId).checked, isTrue);
        semantics.dispose();
      },
    );

    testWidgets('lists the last checked item first in the checked section', (
      tester,
    ) async {
      var now = DateTime.utc(2026, 11, 28, 10);
      final container = await openShoppingListTab(
        tester,
        debugBuild: false,
        clock: Clock(() => now),
      );
      await addItem(tester, container, 'Butter', MarketCategory.dairy);
      await addItem(tester, container, 'Brot', MarketCategory.bakery);

      await tester.tap(
        find.bySemanticsLabel(shoppingList.checkItem(name: 'Butter')),
      );
      await tester.pumpAndSettle();
      now = now.add(const Duration(seconds: 1));
      await tester.tap(
        find.bySemanticsLabel(shoppingList.checkItem(name: 'Brot')),
      );
      await tester.pumpAndSettle();

      expect(topOf(tester, 'Brot'), lessThan(topOf(tester, 'Butter')));
    });

    testWidgets(
      'tapping the checked section header collapses and expands the section',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        await check(
          tester,
          container,
          await addItem(tester, container, 'Kaffee', MarketCategory.beverages),
        );
        final header = find.text(shoppingList.checkedSection(count: 1));

        await tester.tap(header);
        await tester.pumpAndSettle();
        expect(find.text('Kaffee'), findsNothing);

        await tester.tap(header);
        await tester.pumpAndSettle();
        expect(find.text('Kaffee'), findsOneWidget);
      },
    );

    testWidgets('collapsing the checked section stores the collapsed state', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      await check(
        tester,
        container,
        await addItem(tester, container, 'Kaffee', MarketCategory.beverages),
      );

      await tester.tap(find.text(shoppingList.checkedSection(count: 1)));
      await tester.pumpAndSettle();

      expect(
        container
            .read(sharedPreferencesProvider)
            .getBool(PreferenceKeys.shoppingListCheckedExpanded),
        isFalse,
      );
    });

    testWidgets(
      'with a stored collapsed state keeps the checked section collapsed',
      (tester) async {
        final container = await openShoppingListTab(
          tester,
          debugBuild: false,
          storedPreferences: {
            PreferenceKeys.shoppingListCheckedExpanded: false,
          },
        );

        await check(
          tester,
          container,
          await addItem(tester, container, 'Kaffee', MarketCategory.beverages),
        );

        expect(
          find.text(shoppingList.checkedSection(count: 1)),
          findsOneWidget,
        );
        expect(find.text('Kaffee'), findsNothing);
      },
    );

    testWidgets('updates the progress when an item is checked', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: true);
      final progress = shoppingList.progress;
      expect(find.text(progress(checked: 2, total: 16)), findsOneWidget);

      final list = container.read(currentShoppingListProvider).requireValue!;
      await check(
        tester,
        container,
        list.items.firstWhere((item) => !item.checked).id,
      );

      expect(find.text(progress(checked: 3, total: 16)), findsOneWidget);
    });
  });
}
