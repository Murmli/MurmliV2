import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/storage/preference_keys.dart';
import '../../../core/storage/shared_preferences_provider.dart';
import '../app_language.dart';
import 'app_language_provider.dart';
import 'theme_mode_provider.dart';

part 'account_deletion_provider.g.dart';

const _preferenceKeysKeptAfterDeletion = {
  PreferenceKeys.crashReportingEnabled,
  PreferenceKeys.installed,
};

// keepAlive keeps ref usable after the request, the caller only reads it.
@Riverpod(keepAlive: true)
Future<void> Function() deleteAccount(Ref ref) => () async {
  if (!await ref.read(signedInAccountProvider.notifier).deleteAccount()) {
    return;
  }
  final preferences = ref.read(sharedPreferencesProvider);
  for (final key in PreferenceKeys.all.difference(
    _preferenceKeysKeptAfterDeletion,
  )) {
    await preferences.remove(key);
  }
  ref
    ..invalidate(selectedAppLanguageProvider)
    ..invalidate(appThemeModeProvider);
  await AppLanguage.system.apply();
};
