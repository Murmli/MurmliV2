import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/home/tabs/shopping_list_page.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/pump_app.dart';

void main() {
  final translations = AppLocale.de.buildSync().home.tabs;

  group('HomePage', () {
    testWidgets('tapping a navigation bar tab shows that tab with its header', (
      tester,
    ) async {
      await pumpApp(
        tester,
        storedPreferences: {PreferenceKeys.onboardingCompleted: true},
      );

      await tester.tap(find.text(translations.shoppingList.label));
      await tester.pumpAndSettle();

      expect(find.byType(ShoppingListPage), findsOneWidget);
      expect(find.text(translations.shoppingList.title), findsWidgets);
      expect(find.text(translations.chat.title), findsNothing);
    });

    testWidgets(
      'activating a navigation bar tab through the screen reader shows that tab',
      (tester) async {
        final semantics = tester.ensureSemantics();
        await pumpApp(
          tester,
          storedPreferences: {PreferenceKeys.onboardingCompleted: true},
        );

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
