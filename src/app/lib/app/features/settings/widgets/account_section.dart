import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/widgets/confirmation_dialog.dart';
import '../../../core/widgets/message_dialog.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class AccountSection extends HookConsumerWidget {
  const AccountSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.account;
    final isSigningOut = useState(false);
    return SettingsSection(
      title: translations.section,
      children: [
        SettingsRow(
          icon: Icons.logout,
          label: translations.signOut,
          trailing: isSigningOut.value
              ? const SizedBox.square(
                  dimension: 16,
                  child: CircularProgressIndicator(),
                )
              : null,
          onTap: isSigningOut.value
              ? null
              : () => unawaited(_signOut(context, ref, isSigningOut)),
        ),
      ],
    );
  }

  Future<void> _signOut(
    BuildContext context,
    WidgetRef ref,
    ValueNotifier<bool> isSigningOut,
  ) async {
    final signedInAccount = ref.read(signedInAccountProvider.notifier);
    final result = await _whileSigningOut(
      context,
      isSigningOut,
      signedInAccount.signOut,
    );
    if (result == SignOutResult.signedOut || !context.mounted) {
      return;
    }
    final offlineTranslations = context.t.settings.account.signOutOfflineDialog;
    final isConfirmed = await showConfirmationDialog(
      context,
      title: offlineTranslations.title,
      message: offlineTranslations.message,
      cancelLabel: offlineTranslations.cancel,
      confirmLabel: offlineTranslations.confirm,
    );
    if (!isConfirmed || !context.mounted) {
      return;
    }
    final isSignedOut = await _whileSigningOut(
      context,
      isSigningOut,
      signedInAccount.signOutLocally,
    );
    if (isSignedOut || !context.mounted) {
      return;
    }
    final failedTranslations = context.t.settings.account.signOutFailedDialog;
    await showMessageDialog(
      context,
      title: failedTranslations.title,
      message: failedTranslations.message,
      confirmLabel: failedTranslations.confirm,
    );
  }

  Future<T> _whileSigningOut<T>(
    BuildContext context,
    ValueNotifier<bool> isSigningOut,
    Future<T> Function() signOut,
  ) async {
    isSigningOut.value = true;
    try {
      return await signOut();
    } finally {
      if (context.mounted) {
        isSigningOut.value = false;
      }
    }
  }
}
