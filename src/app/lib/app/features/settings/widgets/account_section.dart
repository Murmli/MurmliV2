import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/widgets/confirmation_dialog.dart';
import '../../../core/widgets/message_dialog.dart';
import '../providers/account_actions_provider.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class AccountSection extends ConsumerWidget {
  const AccountSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.account;
    final runningAction = ref.watch(accountActionsProvider);
    return SettingsSection(
      title: translations.section,
      children: [
        SettingsRow(
          icon: Icons.logout,
          label: translations.signOut,
          trailing: runningAction == AccountAction.signOut
              ? const _ProgressIndicator()
              : null,
          onTap: runningAction != null
              ? null
              : () => unawaited(_signOut(context, ref)),
        ),
        SettingsRow(
          icon: Icons.delete_outline,
          label: translations.deleteAccount,
          trailing: runningAction == AccountAction.deletion
              ? const _ProgressIndicator()
              : null,
          onTap: runningAction != null
              ? null
              : () => unawaited(_deleteAccount(context, ref)),
        ),
      ],
    );
  }

  Future<void> _signOut(BuildContext context, WidgetRef ref) async {
    final accountActions = ref.read(accountActionsProvider.notifier);
    final result = await accountActions.signOut();
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
    final isSignedOut = await accountActions.signOutLocally();
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

  Future<void> _deleteAccount(BuildContext context, WidgetRef ref) async {
    final accountActions = ref.read(accountActionsProvider.notifier);
    final translations = context.t.settings.account.deleteDialog;
    final isConfirmed = await showConfirmationDialog(
      context,
      title: translations.title,
      message: translations.message,
      cancelLabel: translations.cancel,
      confirmLabel: translations.confirm,
    );
    if (isConfirmed) {
      await accountActions.delete();
    }
  }
}

class _ProgressIndicator extends StatelessWidget {
  const _ProgressIndicator();

  @override
  Widget build(BuildContext context) =>
      const SizedBox.square(dimension: 16, child: CircularProgressIndicator());
}
