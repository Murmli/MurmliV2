import 'package:material_ui/material_ui.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../core/storage/preference_keys.dart';
import '../../core/storage/shared_preferences_provider.dart';

part 'theme_mode_provider.g.dart';

@Riverpod(keepAlive: true)
class AppThemeMode extends _$AppThemeMode {
  @override
  ThemeMode build() {
    final storedName = ref
        .watch(sharedPreferencesProvider)
        .getString(PreferenceKeys.themeMode);
    return ThemeMode.values.asNameMap()[storedName] ?? ThemeMode.system;
  }

  Future<void> select(ThemeMode themeMode) async {
    await ref
        .read(sharedPreferencesProvider)
        .setString(PreferenceKeys.themeMode, themeMode.name);
    state = themeMode;
  }
}
