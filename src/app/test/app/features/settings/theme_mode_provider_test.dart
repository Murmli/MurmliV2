import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/settings/theme_mode_provider.dart';

import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';

void main() {
  group('AppThemeMode', () {
    test('build without stored value returns system', () async {
      final container = createContainer(await createPreferences());

      expect(container.read(appThemeModeProvider), ThemeMode.system);
    });

    test(
      'select persists the theme mode so a new container restores it',
      () async {
        final container = createContainer(await createPreferences());

        await container
            .read(appThemeModeProvider.notifier)
            .select(ThemeMode.dark);

        expect(container.read(appThemeModeProvider), ThemeMode.dark);
        final restartedContainer = createContainer(await reopenPreferences());
        expect(restartedContainer.read(appThemeModeProvider), ThemeMode.dark);
      },
    );

    test('build with unknown stored value falls back to system', () async {
      final container = createContainer(
        await createPreferences({PreferenceKeys.themeMode: 'sepia'}),
      );

      expect(container.read(appThemeModeProvider), ThemeMode.system);
    });
  });
}
