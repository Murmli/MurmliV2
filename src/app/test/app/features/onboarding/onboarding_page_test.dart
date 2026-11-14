import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/home/home_page.dart';
import 'package:app/app/features/onboarding/onboarding_page.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/preferences.dart';
import '../../../helpers/pump_app.dart';

void main() {
  final translations = AppLocale.de.buildSync().onboarding;

  group('OnboardingPage', () {
    for (final signInLabel in [
      translations.continueWithApple,
      translations.continueWithGoogle,
    ]) {
      testWidgets('tapping "$signInLabel" opens home and persists completion', (
        tester,
      ) async {
        await pumpApp(tester);

        final signInButton = find.text(signInLabel);
        await tester.ensureVisible(signInButton);
        await tester.pump();
        await tester.tap(signInButton);
        await tester.pumpAndSettle();

        expect(find.byType(HomePage), findsOneWidget);
        expect(find.byType(OnboardingPage), findsNothing);
        final storedPreferences = await reopenPreferences();
        expect(
          storedPreferences.getBool(PreferenceKeys.onboardingCompleted),
          isTrue,
        );
      });
    }
  });
}
