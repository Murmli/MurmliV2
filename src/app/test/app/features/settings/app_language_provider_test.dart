import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/settings/app_language.dart';
import 'package:app/app/features/settings/app_language_provider.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';

void main() {
  tearDown(() => LocaleSettings.setLocale(AppLocale.de));

  group('SelectedAppLanguage', () {
    test('build without stored value returns system', () async {
      final container = createContainer(await createPreferences());

      expect(container.read(selectedAppLanguageProvider), AppLanguage.system);
    });

    test('select english switches the app locale to english', () async {
      await LocaleSettings.setLocale(AppLocale.de);
      final container = createContainer(await createPreferences());

      await container
          .read(selectedAppLanguageProvider.notifier)
          .select(AppLanguage.english);

      expect(container.read(selectedAppLanguageProvider), AppLanguage.english);
      expect(LocaleSettings.currentLocale, AppLocale.en);
    });

    test('select german switches the app locale to german', () async {
      await LocaleSettings.setLocale(AppLocale.en);
      final container = createContainer(await createPreferences());

      await container
          .read(selectedAppLanguageProvider.notifier)
          .select(AppLanguage.german);

      expect(container.read(selectedAppLanguageProvider), AppLanguage.german);
      expect(LocaleSettings.currentLocale, AppLocale.de);
    });

    test(
      'select persists the language so a new container restores it',
      () async {
        final container = createContainer(await createPreferences());

        await container
            .read(selectedAppLanguageProvider.notifier)
            .select(AppLanguage.english);

        final restartedContainer = createContainer(await reopenPreferences());
        expect(
          restartedContainer.read(selectedAppLanguageProvider),
          AppLanguage.english,
        );
      },
    );

    test('build with unknown stored value falls back to system', () async {
      final container = createContainer(
        await createPreferences({PreferenceKeys.appLanguage: 'klingon'}),
      );

      expect(container.read(selectedAppLanguageProvider), AppLanguage.system);
    });
  });
}
