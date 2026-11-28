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
import 'package:app/app/features/shopping_list/widgets/shopping_list_checked_section_sliver.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_empty_state.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_entry_field.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_group_sliver.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_item_row.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_pending_entries.dart';
import 'package:app/app/features/shopping_list/widgets/shopping_list_progress_counter.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../../helpers/pump_app.dart';

void main() {
  final translations = AppLocale.de.buildSync();
  final shoppingList = translations.shoppingList;
  final categories = shoppingList.categories;
  final title = translations.home.tabs.shoppingList.title;

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

  // The entry field's own Scrollable lies inside the list, the list's comes first.
  final listScrollable = find
      .descendant(
        of: find.byType(CustomScrollView),
        matching: find.byType(Scrollable),
      )
      .first;

  double topOf(WidgetTester tester, String text) =>
      tester.getTopLeft(find.text(text)).dy;

  Finder rowOf(String itemName) => find.ancestor(
    of: find.text(itemName),
    matching: find.byType(ShoppingListItemRow),
  );

  Finder inEntryField(Finder finder) => find.descendant(
    of: find.byType(ShoppingListEntryField),
    matching: finder,
  );

  Finder inCheckedSection(String itemName) => find.descendant(
    of: find.byType(ShoppingListCheckedSectionSliver),
    matching: find.text(itemName),
  );

  Finder inOpenGroups(String itemName) => find.descendant(
    of: find.byType(ShoppingListGroupSliver),
    matching: find.text(itemName),
  );

  List<ShoppingListItem> storedItems(ProviderContainer container) =>
      container.read(currentShoppingListProvider).requireValue!.items;

  Future<void> enterAndSubmit(WidgetTester tester, String text) async {
    await tester.enterText(find.byType(TextField), text);
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pump();
  }

  Future<void> finishParsing(WidgetTester tester) async {
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
  }

  // The stored toggle arrives in one frame, its animations start ticking in the next.
  Future<void> pumpIntoToggleAnimation(WidgetTester tester) async {
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
  }

  Finder inPendingEntries(String text) => find.descendant(
    of: find.byType(ShoppingListPendingEntries),
    matching: find.text(text),
  );

  ShoppingListItem storedItem(ProviderContainer container, String itemId) =>
      container
          .read(currentShoppingListProvider)
          .requireValue!
          .items
          .singleWhere((item) => item.id == itemId);

  final editor = shoppingList.editor;

  final quickEditor = shoppingList.quickEditor;

  Future<void> openQuickEditor(WidgetTester tester, String itemName) async {
    await tester.tap(find.text(itemName));
    await tester.pumpAndSettle();
  }

  Future<void> openEditor(WidgetTester tester, String itemName) async {
    await tester.longPress(find.text(itemName));
    await tester.pumpAndSettle();
  }

  Finder editorField(String label) => find.widgetWithText(TextField, label);

  Future<void> selectEntry(
    WidgetTester tester,
    Finder dropdownMenu,
    String entry,
  ) async {
    await tester.ensureVisible(dropdownMenu);
    await tester.tap(dropdownMenu);
    await tester.pumpAndSettle();
    final entryText = find.text(entry).last;
    await tester.ensureVisible(entryText);
    await tester.pumpAndSettle();
    await tester.tap(entryText);
    await tester.pumpAndSettle();
  }

  Future<void> tapSave(WidgetTester tester) async {
    // Rebuilds the editor so the button's callback sees the entered text.
    await tester.pump();
    final saveButton = find.widgetWithText(FilledButton, editor.save);
    await tester.ensureVisible(saveButton);
    await tester.tap(saveButton);
    await tester.pumpAndSettle();
  }

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
        await tester.scrollUntilVisible(
          find.text('Sauerrahm'),
          100,
          scrollable: listScrollable,
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
      expect(find.byType(ShoppingListGroupSliver), findsNothing);
    });

    testWidgets(
      'without items shows the title, the entry field and the empty state but no progress',
      (tester) async {
        await openShoppingListTab(tester, debugBuild: false);

        expect(find.byType(ShoppingListProgressCounter), findsNothing);
        expect(find.text(title), findsOneWidget);
        expect(find.byType(ShoppingListEntryField), findsOneWidget);
        expect(find.byType(ShoppingListEmptyState), findsOneWidget);
      },
    );

    testWidgets(
      'hides the header while scrolling down, keeps the entry field and shows the header again when scrolling back',
      (tester) async {
        await openShoppingListTab(tester, debugBuild: true);
        final scrollView = find.byType(CustomScrollView);

        await tester.drag(scrollView, const Offset(0, -300));
        await tester.pumpAndSettle();

        expect(find.text(title).hitTestable(), findsNothing);
        expect(find.byType(TextField).hitTestable(), findsOneWidget);

        await tester.drag(scrollView, const Offset(0, 60));
        await tester.pumpAndSettle();

        expect(find.text(title).hitTestable(), findsOneWidget);
        expect(find.byType(TextField).hitTestable(), findsOneWidget);
      },
    );

    testWidgets('pins the category header below the entry field', (
      tester,
    ) async {
      await openShoppingListTab(tester, debugBuild: true);

      await tester.drag(find.byType(CustomScrollView), const Offset(0, -80));
      await tester.pumpAndSettle();

      expect(
        topOf(tester, categories.vegetables),
        greaterThanOrEqualTo(
          tester.getBottomLeft(find.byType(ShoppingListEntryField)).dy - 1,
        ),
      );
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
        expect(find.text('1 / 2'), findsOneWidget);
        expect(storedItem(container, itemId).checked, isTrue);
        expect(find.text(shoppingList.item.delete), findsNothing);
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
            of: find.byType(ShoppingListGroupSliver),
            matching: find.text('Butter'),
          ),
          findsOneWidget,
        );
        expect(find.byType(ShoppingListCheckedSectionSliver), findsNothing);
        expect(storedItem(container, itemId).checked, isFalse);
      },
    );

    testWidgets(
      'tapping the tile of an open item stores it as checked and shows it in both sections until the old row has disappeared',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Butter',
          MarketCategory.dairy,
        );

        await tester.tap(
          find.bySemanticsLabel(shoppingList.checkItem(name: 'Butter')),
        );
        await tester.pump();

        expect(storedItem(container, itemId).checked, isTrue);

        await pumpIntoToggleAnimation(tester);

        expect(inOpenGroups('Butter'), findsOneWidget);
        expect(inCheckedSection('Butter'), findsOneWidget);

        await tester.pumpAndSettle();

        expect(inOpenGroups('Butter'), findsNothing);
        expect(inCheckedSection('Butter'), findsOneWidget);
      },
    );

    testWidgets(
      'tapping the tile of a checked item stores it as open and shows it in both sections until the old row has disappeared',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
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
        await tester.pump();

        expect(storedItem(container, itemId).checked, isFalse);

        await pumpIntoToggleAnimation(tester);

        expect(inOpenGroups('Butter'), findsOneWidget);
        expect(inCheckedSection('Butter'), findsOneWidget);

        await tester.pumpAndSettle();

        expect(inOpenGroups('Butter'), findsOneWidget);
        expect(inCheckedSection('Butter'), findsNothing);
      },
    );

    testWidgets(
      'checking the only item of a category keeps its group until the group has collapsed',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        await addItem(tester, container, 'Milch', MarketCategory.dairy);
        await addItem(tester, container, 'Karotten', MarketCategory.vegetables);

        await tester.tap(
          find.bySemanticsLabel(shoppingList.checkItem(name: 'Milch')),
        );
        await tester.pump();
        await pumpIntoToggleAnimation(tester);

        expect(find.text(categories.dairy), findsOneWidget);

        await tester.pumpAndSettle();

        expect(find.text(categories.dairy), findsNothing);
        expect(find.text(categories.vegetables), findsOneWidget);
      },
    );

    testWidgets(
      'reopening the only checked item keeps the checked section until it has collapsed',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        await check(
          tester,
          container,
          await addItem(tester, container, 'Kaffee', MarketCategory.beverages),
        );

        await tester.tap(
          find.bySemanticsLabel(shoppingList.uncheckItem(name: 'Kaffee')),
        );
        await tester.pump();
        await pumpIntoToggleAnimation(tester);

        expect(find.byType(ShoppingListCheckedSectionSliver), findsOneWidget);

        await tester.pumpAndSettle();

        expect(find.byType(ShoppingListCheckedSectionSliver), findsNothing);
      },
    );

    testWidgets(
      'checking the first item shows the checked section with the item',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        await addItem(tester, container, 'Milch', MarketCategory.dairy);
        await addItem(tester, container, 'Karotten', MarketCategory.vegetables);

        await tester.tap(
          find.bySemanticsLabel(shoppingList.checkItem(name: 'Milch')),
        );
        await tester.pumpAndSettle();

        expect(
          find.text(shoppingList.checkedSection(count: 1)),
          findsOneWidget,
        );
        expect(inCheckedSection('Milch'), findsOneWidget);
      },
    );

    testWidgets(
      'while the old row of a checked item disappears the counts include only the stored items',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        await addItem(tester, container, 'Butter', MarketCategory.dairy);
        await addItem(tester, container, 'Käse', MarketCategory.dairy);

        await tester.tap(
          find.bySemanticsLabel(shoppingList.checkItem(name: 'Butter')),
        );
        await tester.pump();
        await pumpIntoToggleAnimation(tester);

        expect(inEntryField(find.text('1 / 2')), findsOneWidget);
        expect(
          find.text(shoppingList.checkedSection(count: 1)),
          findsOneWidget,
        );
        expect(
          find.descendant(
            of: find.byType(ShoppingListGroupSliver),
            matching: find.text('1'),
          ),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'with reduced motion tapping the tile moves the item to the checked section at once',
      (tester) async {
        tester.platformDispatcher.accessibilityFeaturesTestValue =
            const FakeAccessibilityFeatures(disableAnimations: true);
        addTearDown(
          tester.platformDispatcher.clearAccessibilityFeaturesTestValue,
        );
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Butter',
          MarketCategory.dairy,
        );

        await tester.tap(
          find.bySemanticsLabel(shoppingList.checkItem(name: 'Butter')),
        );
        await tester.pump();

        expect(storedItem(container, itemId).checked, isTrue);
        expect(inOpenGroups('Butter'), findsNothing);
        expect(inCheckedSection('Butter'), findsOneWidget);
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

    testWidgets(
      'shows the progress as a counter in the entry field and announces it in words',
      (tester) async {
        await openShoppingListTab(tester, debugBuild: true);

        expect(inEntryField(find.text('2 / 16')), findsOneWidget);
        expect(
          find.bySemanticsLabel(shoppingList.progress(checked: 2, total: 16)),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'hides the progress while text is entered and shows it again after submitting',
      (tester) async {
        await openShoppingListTab(tester, debugBuild: true);

        await tester.enterText(find.byType(TextField), 'Milch');
        await tester.pumpAndSettle();

        expect(
          find.semantics.byLabel(shoppingList.progress(checked: 2, total: 16)),
          findsNothing,
        );
        expect(find.text('2 / 16').hitTestable(), findsNothing);

        await tester.testTextInput.receiveAction(TextInputAction.done);
        await finishParsing(tester);

        expect(
          find.semantics.byLabel(shoppingList.progress(checked: 2, total: 17)),
          findsOne,
        );
        expect(inEntryField(find.text('2 / 17')).hitTestable(), findsOneWidget);
      },
    );

    testWidgets(
      'with large text shows the progress without spaces around the slash',
      (tester) async {
        tester.platformDispatcher.textScaleFactorTestValue = 2;
        addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
        await openShoppingListTab(tester, debugBuild: true);

        expect(inEntryField(find.text('2/16')), findsOneWidget);
      },
    );

    testWidgets('updates the progress when an item is checked', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: true);
      expect(find.text('2 / 16'), findsOneWidget);

      final list = container.read(currentShoppingListProvider).requireValue!;
      await check(
        tester,
        container,
        list.items.firstWhere((item) => !item.checked).id,
      );

      expect(find.text('3 / 16'), findsOneWidget);
    });
    testWidgets(
      'submitting free text shows it as pending and then adds the parsed item to its category',
      (tester) async {
        await openShoppingListTab(tester, debugBuild: false);

        await enterAndSubmit(tester, '2 Liter Milch');

        expect(inPendingEntries('2 Liter Milch'), findsOneWidget);
        expect(
          tester.widget<TextField>(find.byType(TextField)).controller!.text,
          isEmpty,
        );
        expect(find.text(shoppingList.empty.title), findsNothing);

        await finishParsing(tester);

        expect(
          find.descendant(of: rowOf('Milch'), matching: find.text('2 l')),
          findsOneWidget,
        );
        expect(find.text(categories.dairy), findsOneWidget);
        expect(find.byType(ShoppingListPendingEntries), findsNothing);
      },
    );

    testWidgets('free text with several items adds each to its category', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);

      await enterAndSubmit(tester, 'Bananen, Brot und Tomaten');
      await finishParsing(tester);

      expect(find.text(categories.fruits), findsOneWidget);
      expect(find.text(categories.bakery), findsOneWidget);
      expect(find.text(categories.vegetables), findsOneWidget);
      expect(
        storedItems(container).map((item) => item.name),
        unorderedEquals(['Bananen', 'Brot', 'Tomaten']),
      );
    });

    testWidgets(
      'unknown free text adds one item named after the text under miscellaneous',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);

        await enterAndSubmit(tester, 'Käse und Wurst');
        await finishParsing(tester);

        expect(storedItems(container).map((item) => item.name), [
          'Käse und Wurst',
        ]);
        expect(find.text(categories.miscellaneous), findsOneWidget);
      },
    );

    testWidgets('submitting only spaces adds nothing', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);

      await enterAndSubmit(tester, '   ');

      expect(find.byType(ShoppingListPendingEntries), findsNothing);
      await finishParsing(tester);
      expect(storedItems(container), isEmpty);
      expect(find.text(shoppingList.empty.title), findsOneWidget);
    });

    testWidgets(
      'a second submission before the first is parsed keeps both pending and adds both',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);

        await enterAndSubmit(tester, '2 Liter Milch');
        await tester.pump(const Duration(milliseconds: 300));
        await enterAndSubmit(tester, '500g Hackfleisch');

        expect(inPendingEntries('2 Liter Milch'), findsOneWidget);
        expect(inPendingEntries('500g Hackfleisch'), findsOneWidget);

        await finishParsing(tester);

        expect(
          storedItems(container).map((item) => item.name),
          unorderedEquals(['Milch', 'Hackfleisch']),
        );
      },
    );

    testWidgets('keeps the keyboard open after submitting', (tester) async {
      await openShoppingListTab(tester, debugBuild: false);

      await tester.tap(find.byType(TextField));
      await enterAndSubmit(tester, '2 Liter Milch');
      await finishParsing(tester);

      expect(tester.testTextInput.isVisible, isTrue);
    });

    testWidgets('tapping the add button adds the entered item', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final addButton = find.byTooltip(shoppingList.entry.add);
      expect(addButton, findsNothing);

      await tester.enterText(find.byType(TextField), '500g Hackfleisch');
      await tester.pump();
      await tester.tap(addButton);
      await finishParsing(tester);

      expect(storedItems(container).map((item) => item.name), ['Hackfleisch']);
    });
    testWidgets(
      'keeps the header of a category on top while scrolling through its items until the next category pushes it away',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: true);
        final groups = container.read(shoppingListOverviewProvider)!.groups;
        final firstHeader = groups[0].category.label(translations);
        final secondHeader = groups[1].category.label(translations);
        final scrollView = find.byType(CustomScrollView);
        final pinnedTop =
            tester.getTopLeft(scrollView).dy +
            tester.getSize(find.byType(ShoppingListEntryField)).height;
        double cardTopOf(int groupIndex) =>
            tester.getTopLeft(rowOf(groups[groupIndex].items.first.name)).dy;

        await tester.drag(
          scrollView,
          Offset(0, -(cardTopOf(0) - pinnedTop + 24)),
        );
        await tester.pumpAndSettle();

        expect(cardTopOf(0), lessThan(pinnedTop));
        expect(topOf(tester, firstHeader), moreOrLessEquals(pinnedTop));

        await tester.drag(
          scrollView,
          Offset(0, -(cardTopOf(1) - pinnedTop + 24)),
        );
        await tester.pumpAndSettle();

        expect(topOf(tester, secondHeader), moreOrLessEquals(pinnedTop));
        expect(
          find.text(firstHeader).evaluate().isEmpty ||
              tester.getBottomLeft(find.text(firstHeader)).dy <= pinnedTop,
          isTrue,
        );
      },
    );

    testWidgets('swiping a row to the start removes the item', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      await addItem(tester, container, 'Butter', MarketCategory.dairy);
      await addItem(tester, container, 'Brot', MarketCategory.bakery);
      final row = rowOf('Butter');

      await tester.drag(row, Offset(-tester.getSize(row).width, 0));
      await tester.pumpAndSettle();

      expect(find.text('Butter'), findsNothing);
      expect(storedItems(container).map((item) => item.name), ['Brot']);
      expect(tester.takeException(), isNull);
    });
  });

  group('quick editor', () {
    Finder quickSave() => find.byTooltip(editor.save);

    testWidgets(
      'tapping the name of an open item opens the quick editor with its values and the name focused without checking it',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Milch',
          MarketCategory.dairy,
          quantity: 1.5,
          unit: MeasurementUnit.liter,
        );

        await openQuickEditor(tester, 'Milch');

        final nameField = find.descendant(
          of: editorField(editor.name),
          matching: find.byType(EditableText),
        );
        expect(
          tester.widget<EditableText>(nameField).focusNode.hasFocus,
          isTrue,
        );
        expect(tester.testTextInput.isVisible, isTrue);
        expect(
          tester.widget<TextField>(editorField(editor.name)).controller!.text,
          'Milch',
        );
        expect(
          tester
              .widget<TextField>(editorField(editor.quantity))
              .controller!
              .text,
          '1,5',
        );
        expect(
          find.descendant(
            of: editorField(editor.quantity),
            matching: find.text(shoppingList.units.liter.abbreviation),
          ),
          findsOneWidget,
        );
        expect(storedItem(container, itemId).checked, isFalse);
      },
    );

    testWidgets(
      'changing name and quantity and pressing done stores them and keeps the unit',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Milch',
          MarketCategory.dairy,
          quantity: 1,
          unit: MeasurementUnit.liter,
        );

        await openQuickEditor(tester, 'Milch');
        await tester.enterText(editorField(editor.name), 'Hafermilch');
        await tester.enterText(editorField(editor.quantity), '1,5');
        await tester.pump();
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pumpAndSettle();

        expect(quickSave(), findsNothing);
        final item = storedItem(container, itemId);
        expect(item.name, 'Hafermilch');
        expect(item.quantity, 1.5);
        expect(item.unit, MeasurementUnit.liter);
        expect(item.category, MarketCategory.dairy);
      },
    );

    testWidgets('a name of only spaces is not stored', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final itemId = await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
      );

      await openQuickEditor(tester, 'Milch');
      await tester.enterText(editorField(editor.name), '   ');
      await tester.pump();
      await tester.tap(quickSave());
      await tester.pumpAndSettle();

      expect(quickSave(), findsOneWidget);
      expect(storedItem(container, itemId).name, 'Milch');
    });

    testWidgets(
      'a quantity of 0 shows an error and pressing done stores nothing',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Milch',
          MarketCategory.dairy,
          quantity: 2,
        );

        await openQuickEditor(tester, 'Milch');
        await tester.enterText(editorField(editor.quantity), '0');
        await tester.pump();
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pumpAndSettle();

        expect(find.text(editor.invalidQuantity), findsOneWidget);
        expect(quickSave(), findsOneWidget);
        expect(storedItem(container, itemId).quantity, 2);
      },
    );

    testWidgets(
      'choosing a category through the tile and saving moves the item to it',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Orangensaft',
          MarketCategory.dairy,
        );

        await openQuickEditor(tester, 'Orangensaft');
        await tester.tap(find.byTooltip(quickEditor.category));
        await tester.pumpAndSettle();
        final beverages = find.widgetWithText(
          MenuItemButton,
          categories.beverages,
        );
        await tester.ensureVisible(beverages);
        await tester.pumpAndSettle();
        await tester.tap(beverages);
        await tester.pumpAndSettle();
        await tester.tap(quickSave());
        await tester.pumpAndSettle();

        expect(
          storedItem(container, itemId).category,
          MarketCategory.beverages,
        );
        expect(
          find.descendant(
            of: find.ancestor(
              of: find.text(categories.beverages),
              matching: find.byType(ShoppingListGroupSliver),
            ),
            matching: find.text('Orangensaft'),
          ),
          findsOneWidget,
        );
        expect(find.text(categories.dairy), findsNothing);
      },
    );

    testWidgets('the delete button removes the item right away', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      await addItem(tester, container, 'Butter', MarketCategory.dairy);
      await addItem(tester, container, 'Brot', MarketCategory.bakery);

      await openQuickEditor(tester, 'Butter');
      await tester.tap(find.byTooltip(shoppingList.item.delete));
      await tester.pumpAndSettle();

      expect(quickSave(), findsNothing);
      expect(find.text('Butter'), findsNothing);
      expect(storedItems(container).map((item) => item.name), ['Brot']);
    });

    testWidgets('dismissing the quick editor discards the changes', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final itemId = await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
      );

      await openQuickEditor(tester, 'Milch');
      await tester.enterText(editorField(editor.name), 'Hafermilch');
      await tester.tapAt(const Offset(20, 20));
      await tester.pumpAndSettle();

      expect(quickSave(), findsNothing);
      expect(storedItem(container, itemId).name, 'Milch');
      expect(find.text('Milch'), findsOneWidget);
    });

    testWidgets('details opens the detailed editor', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      await addItem(tester, container, 'Milch', MarketCategory.dairy);

      await openQuickEditor(tester, 'Milch');
      await tester.tap(find.text(quickEditor.details));
      await tester.pumpAndSettle();

      expect(quickSave(), findsNothing);
      expect(find.text(editor.title), findsOneWidget);
    });
  });

  group('detailed editor', () {
    testWidgets('long pressing the name of an item opens the detailed editor', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final itemId = await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
      );

      await openEditor(tester, 'Milch');

      expect(find.text(editor.title), findsOneWidget);
      expect(storedItem(container, itemId).checked, isFalse);
    });

    testWidgets(
      'editing name, quantity, unit and category stores them and moves the item to the new category',
      (tester) async {
        final container = await openShoppingListTab(tester, debugBuild: false);
        final itemId = await addItem(
          tester,
          container,
          'Milch',
          MarketCategory.dairy,
        );

        await openEditor(tester, 'Milch');
        await tester.enterText(editorField(editor.name), 'Orangensaft');
        await tester.enterText(editorField(editor.quantity), '1,5');
        await selectEntry(
          tester,
          find.byType(DropdownMenu<MeasurementUnit?>),
          shoppingList.units.liter.name,
        );
        await selectEntry(
          tester,
          find.byType(DropdownMenu<MarketCategory>),
          categories.beverages,
        );
        await tapSave(tester);

        expect(find.text(editor.title), findsNothing);
        expect(
          find.descendant(
            of: rowOf('Orangensaft'),
            matching: find.text('1,5 l'),
          ),
          findsOneWidget,
        );
        expect(
          find.descendant(
            of: find.ancestor(
              of: find.text(categories.beverages),
              matching: find.byType(ShoppingListGroupSliver),
            ),
            matching: find.text('Orangensaft'),
          ),
          findsOneWidget,
        );
        expect(find.text(categories.dairy), findsNothing);
        final item = storedItem(container, itemId);
        expect(item.name, 'Orangensaft');
        expect(item.quantity, 1.5);
        expect(item.unit, MeasurementUnit.liter);
        expect(item.category, MarketCategory.beverages);
      },
    );

    testWidgets('removing quantity and unit stores the item without both', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final itemId = await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
        quantity: 2,
        unit: MeasurementUnit.liter,
      );

      await openEditor(tester, 'Milch');
      await tester.enterText(editorField(editor.quantity), '');
      await selectEntry(
        tester,
        find.byType(DropdownMenu<MeasurementUnit?>),
        editor.noUnit,
      );
      await tapSave(tester);

      final item = storedItem(container, itemId);
      expect(item.quantity, isNull);
      expect(item.unit, isNull);
      expect(
        find.descendant(of: rowOf('Milch'), matching: find.byType(Text)),
        findsOneWidget,
      );
    });

    testWidgets('a name of only spaces cannot be saved', (tester) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final itemId = await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
      );

      await openEditor(tester, 'Milch');
      await tester.enterText(editorField(editor.name), '   ');
      await tapSave(tester);

      expect(find.text(editor.title), findsOneWidget);
      expect(storedItem(container, itemId).name, 'Milch');
    });

    testWidgets('a quantity of 0 shows an error and cannot be saved', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: false);
      final itemId = await addItem(
        tester,
        container,
        'Milch',
        MarketCategory.dairy,
        quantity: 2,
      );

      await openEditor(tester, 'Milch');
      await tester.enterText(editorField(editor.quantity), '0');
      await tester.pump();

      expect(find.text(editor.invalidQuantity), findsOneWidget);
      await tapSave(tester);
      expect(find.text(editor.title), findsOneWidget);
      expect(storedItem(container, itemId).quantity, 2);
    });

    testWidgets('editing an item with recipes keeps its recipe shares', (
      tester,
    ) async {
      final container = await openShoppingListTab(tester, debugBuild: true);
      final before = storedItems(container)
          .singleWhere((item) => item.name == 'Sauerrahm');
      await tester.scrollUntilVisible(
        find.text('Sauerrahm'),
        100,
        scrollable: listScrollable,
      );
      await Scrollable.ensureVisible(
        tester.element(find.text('Sauerrahm')),
        alignment: 0.5,
      );
      await tester.pumpAndSettle();

      await openEditor(tester, 'Sauerrahm');
      await tester.enterText(editorField(editor.name), 'Saure Sahne');
      await tapSave(tester);

      final item = storedItem(container, before.id);
      expect(item.name, 'Saure Sahne');
      expect(item.recipeShares, hasLength(2));
      expect(item.recipeShares, before.recipeShares);
    });
  });
}
