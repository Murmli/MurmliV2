import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../core/storage/preference_keys.dart';
import '../../core/storage/shared_preferences_provider.dart';
import 'app_language.dart';

part 'app_language_provider.g.dart';

@Riverpod(keepAlive: true)
class SelectedAppLanguage extends _$SelectedAppLanguage {
  @override
  AppLanguage build() {
    final storedName = ref
        .watch(sharedPreferencesProvider)
        .getString(PreferenceKeys.appLanguage);
    return AppLanguage.values.asNameMap()[storedName] ?? AppLanguage.system;
  }

  Future<void> select(AppLanguage language) async {
    await ref
        .read(sharedPreferencesProvider)
        .setString(PreferenceKeys.appLanguage, language.name);
    await language.apply();
    state = language;
  }
}
