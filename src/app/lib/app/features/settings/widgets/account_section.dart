import 'dart:async';

import 'package:dio/dio.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
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
          onTap: () =>
              unawaited(ref.read(signedInAccountProvider.notifier).signOut()),
        ),
        SettingsRow(
          icon: Icons.delete_forever_outlined,
          label: translations.deleteAccount,
          onTap: () => unawaited(_deleteAccount(context, ref)),
        ),
      ],
    );
  }

  Future<void> _deleteAccount(BuildContext context, WidgetRef ref) async {
    final isConfirmed = await showDialog<bool>(
      context: context,
      builder: (_) => const _DeleteAccountDialog(),
    );
    if (isConfirmed != true) {
      return;
    }
    try {
      await ref.read(signedInAccountProvider.notifier).deleteAccount();
    } on DioException {
      // apiClientProvider already showed the error dialog.
    }
  }
}

class _DeleteAccountDialog extends StatelessWidget {
  const _DeleteAccountDialog();

  @override
  Widget build(BuildContext context) {
    final translations = context.t.settings.account;
    return AlertDialog(
      title: Text(translations.deleteTitle),
      content: Text(translations.deleteMessage),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: Text(translations.cancel),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(translations.deleteConfirm),
        ),
      ],
    );
  }
}
