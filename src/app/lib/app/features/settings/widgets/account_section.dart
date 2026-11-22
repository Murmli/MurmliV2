import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/widgets/confirmation_dialog.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class AccountSection extends ConsumerWidget {
  const AccountSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.account;
    return SettingsSection(
      title: translations.section,
      children: [
        SettingsRow(
          icon: Icons.logout,
          label: translations.signOut,
          onTap: () => unawaited(_signOut(context, ref)),
        ),
      ],
    );
  }

  Future<void> _signOut(BuildContext context, WidgetRef ref) async {
    final signedInAccount = ref.read(signedInAccountProvider.notifier);
    if (await signedInAccount.signOut() == SignOutResult.signedOut ||
        !context.mounted) {
      return;
    }
    final translations = context.t.settings.account.signOutOfflineDialog;
    final isConfirmed = await showConfirmationDialog(
      context,
      title: translations.title,
      message: translations.message,
      cancelLabel: translations.cancel,
      confirmLabel: translations.confirm,
    );
    if (isConfirmed) {
      await signedInAccount.signOutLocally();
    }
  }
}
