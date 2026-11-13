import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';
import 'package:package_info_plus/package_info_plus.dart';

import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/onboarding/onboarding_page.dart';
import 'package:app/app/features/settings/settings_page.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/preferences.dart';
import '../../../helpers/pump_app.dart';

void main() {
  final germanTranslations = AppLocale.de.buildSync();
  final translations = germanTranslations.settings;

  Future<void> openSettings(WidgetTester tester) async {
    await pumpApp(
      tester,
      storedPreferences: {PreferenceKeys.onboardingCompleted: true},
    );
    await tester.tap(find.byTooltip(germanTranslations.home.settingsTooltip));
    await tester.pumpAndSettle();
  }

  Future<void> tapRow(WidgetTester tester, String label) async {
    final row = find.text(label);
    await tester.scrollUntilVisible(row, 100);
    await tester.ensureVisible(row);
    await tester.pump();
    await tester.tap(row);
    await tester.pumpAndSettle();
  }

  setUp(() {
    PackageInfo.setMockInitialValues(
      appName: 'Murmli',
      packageName: 'app',
      version: '1.2.3',
      buildNumber: '45',
      buildSignature: '',
    );
  });

  tearDown(() => LocaleSettings.setLocale(AppLocale.de));

  group('SettingsPage', () {
    testWidgets('settings button on home opens the settings page', (
      tester,
    ) async {
      await openSettings(tester);

      expect(find.byType(SettingsPage), findsOneWidget);
    });

    testWidgets('selecting dark switches the app to the dark theme', (
      tester,
    ) async {
      await openSettings(tester);

      await tapRow(tester, translations.theme.dark);

      expect(
        tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode,
        ThemeMode.dark,
      );
    });

    testWidgets('selecting english shows the texts in english', (tester) async {
      await openSettings(tester);

      await tapRow(tester, translations.language.english);

      expect(find.text('Settings'), findsOneWidget);
      expect(find.text('Einstellungen'), findsNothing);
    });

    testWidgets(
      'show onboarding again opens the onboarding and persists it as not completed',
      (tester) async {
        await openSettings(tester);

        await tapRow(tester, translations.onboarding.showAgain);

        expect(find.byType(OnboardingPage), findsOneWidget);
        final storedPreferences = await reopenPreferences();
        expect(
          storedPreferences.getBool(PreferenceKeys.onboardingCompleted),
          isNull,
        );
      },
    );

    testWidgets('about section shows the app version', (tester) async {
      await openSettings(tester);

      await tester.scrollUntilVisible(find.text('1.2.3 (45)'), 100);

      expect(find.text('1.2.3 (45)'), findsOneWidget);
    });
  });
}
