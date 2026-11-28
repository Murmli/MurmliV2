import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/features/home/tabs/shopping_list_page.dart';
import 'package:app/app/features/home/widgets/home_header.dart';
import 'package:app/app/features/shopping_list/providers/first_shopping_list_provider.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/pump_app.dart';

void main() {
  final home = AppLocale.de.buildSync().home;
  final translations = home.tabs;

  group('HomePage', () {
    testWidgets('tapping a navigation bar tab shows that tab with its header', (
      tester,
    ) async {
      (await pumpApp(
        tester,
        signedInAccount: const Account(id: 'account'),
      )).read(firstShoppingListProvider);

      await tester.tap(find.text(translations.shoppingList.label));
      await tester.pumpAndSettle();

      expect(find.byType(ShoppingListPage), findsOneWidget);
      expect(find.text(translations.shoppingList.title), findsWidgets);
      expect(find.text(translations.chat.title), findsNothing);
    });

    testWidgets(
      'a tab without its own header shows its title and the settings button in the header',
      (tester) async {
        await pumpApp(tester, signedInAccount: const Account(id: 'account'));

        await tester.tap(find.text(translations.recipes.label));
        await tester.pumpAndSettle();

        expect(
          find.descendant(
            of: find.byType(HomeHeader),
            matching: find.text(translations.recipes.title),
          ),
          findsOneWidget,
        );
        expect(find.byTooltip(home.settingsTooltip), findsOneWidget);
      },
    );

    testWidgets(
      'activating a navigation bar tab through the screen reader shows that tab',
      (tester) async {
        final semantics = tester.ensureSemantics();
        (await pumpApp(
          tester,
          signedInAccount: const Account(id: 'account'),
        )).read(firstShoppingListProvider);

        tester.semantics.tap(
          find.semantics.byLabel(translations.shoppingList.label),
        );
        await tester.pumpAndSettle();

        expect(find.byType(ShoppingListPage), findsOneWidget);
        semantics.dispose();
      },
    );
  });
}
