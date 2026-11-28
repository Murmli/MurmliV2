import 'package:flutter/foundation.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/flavor/flavor.dart';
import '../../../core/flavor/flavor_provider.dart';
import '../../../core/storage/preference_keys.dart';
import '../../../core/storage/shared_preferences_provider.dart';
import '../../shopping_list/providers/checked_section_expanded_provider.dart';
import '../app_language.dart';
import 'app_language_provider.dart';
import 'apple_disconnect_notice_provider.dart';
import 'theme_mode_provider.dart';

part 'account_actions_provider.g.dart';

enum AccountAction { signOut, deletion }

const _preferenceKeysKeptAfterDeletion = {
  PreferenceKeys.crashReportingEnabled,
  PreferenceKeys.installed,
};

@Riverpod(keepAlive: true)
class AccountActions extends _$AccountActions {
  @override
  AccountAction? build() => null;

  Future<SignOutResult> signOut() => _whileRunning(
    AccountAction.signOut,
    ref.read(signedInAccountProvider.notifier).signOut,
  );

  Future<bool> signOutLocally() => _whileRunning(
    AccountAction.signOut,
    ref.read(signedInAccountProvider.notifier).signOutLocally,
  );

  Future<AccountDeletionResult> delete() =>
      _whileRunning(AccountAction.deletion, () async {
        final result = await ref
            .read(signedInAccountProvider.notifier)
            .deleteAccount();
        if (result != AccountDeletionResult.failed) {
          await _removeDeviceData();
          if (_signsInWithApple()) {
            ref.read(pendingAppleDisconnectNoticeProvider.notifier).raise();
          }
        }
        return result;
      });

  Future<void> _removeDeviceData() async {
    final preferences = ref.read(sharedPreferencesProvider);
    for (final key in PreferenceKeys.all.difference(
      _preferenceKeysKeptAfterDeletion,
    )) {
      await preferences.remove(key);
    }
    ref
      ..invalidate(selectedAppLanguageProvider)
      ..invalidate(appThemeModeProvider)
      ..invalidate(checkedSectionExpandedProvider);
    await AppLanguage.system.apply();
  }

  bool _signsInWithApple() =>
      ref.read(flavorProvider) != Flavor.local &&
      defaultTargetPlatform == TargetPlatform.iOS;

  Future<T> _whileRunning<T>(
    AccountAction action,
    Future<T> Function() run,
  ) async {
    state = action;
    try {
      return await run();
    } finally {
      state = null;
    }
  }
}
