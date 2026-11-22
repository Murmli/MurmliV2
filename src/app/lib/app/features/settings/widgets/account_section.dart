import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/widgets/confirmation_dialog.dart';
import '../../../core/widgets/message_dialog.dart';
import '../providers/account_deletion_provider.dart';
import 'settings_row.dart';
import 'settings_section.dart';

enum _AccountAction { signOut, deletion }

class AccountSection extends HookConsumerWidget {
  const AccountSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.account;
    final runningAction = useState<_AccountAction?>(null);
    return SettingsSection(
      title: translations.section,
      children: [
        SettingsRow(
          icon: Icons.logout,
          label: translations.signOut,
          trailing: runningAction.value == _AccountAction.signOut
              ? const _ProgressIndicator()
              : null,
          onTap: runningAction.value != null
              ? null
              : () => unawaited(_signOut(context, ref, runningAction)),
        ),
        SettingsRow(
          icon: Icons.delete_outline,
          label: translations.deleteAccount,
          trailing: runningAction.value == _AccountAction.deletion
              ? const _ProgressIndicator()
              : null,
          onTap: runningAction.value != null
              ? null
              : () => unawaited(_deleteAccount(context, ref, runningAction)),
        ),
      ],
    );
  }

  Future<void> _signOut(
    BuildContext context,
    WidgetRef ref,
    ValueNotifier<_AccountAction?> runningAction,
  ) async {
    final signedInAccount = ref.read(signedInAccountProvider.notifier);
    final result = await _whileRunning(
      context,
      runningAction,
      _AccountAction.signOut,
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
    final isSignedOut = await _whileRunning(
      context,
      runningAction,
      _AccountAction.signOut,
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

  Future<void> _deleteAccount(
    BuildContext context,
    WidgetRef ref,
    ValueNotifier<_AccountAction?> runningAction,
  ) async {
    final translations = context.t.settings.account.deleteDialog;
    final isConfirmed = await showConfirmationDialog(
      context,
      title: translations.title,
      message: translations.message,
      cancelLabel: translations.cancel,
      confirmLabel: translations.confirm,
    );
    if (isConfirmed && context.mounted) {
      await _whileRunning(
        context,
        runningAction,
        _AccountAction.deletion,
        ref.read(deleteAccountProvider),
      );
    }
  }

  Future<T> _whileRunning<T>(
    BuildContext context,
    ValueNotifier<_AccountAction?> runningAction,
    _AccountAction action,
    Future<T> Function() run,
  ) async {
    runningAction.value = action;
    try {
      return await run();
    } finally {
      if (context.mounted) {
        runningAction.value = null;
      }
    }
  }
}

class _ProgressIndicator extends StatelessWidget {
  const _ProgressIndicator();

  @override
  Widget build(BuildContext context) =>
      const SizedBox.square(dimension: 16, child: CircularProgressIndicator());
}
