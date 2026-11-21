import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/sync/app_database.dart';
import '../providers/shopping_list_repository_provider.dart';

enum _ShoppingListAction { showAccountId, share, leave }

class ShoppingListActionsMenu extends ConsumerWidget {
  const ShoppingListActionsMenu({required this.list, super.key});

  final ShoppingList list;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.shoppingList;
    return PopupMenuButton<_ShoppingListAction>(
      tooltip: translations.actions,
      onSelected: (action) => unawaited(switch (action) {
        _ShoppingListAction.showAccountId => _showAccountId(context, ref),
        _ShoppingListAction.share => _share(context, ref),
        _ShoppingListAction.leave => _leave(context, ref),
      }),
      itemBuilder: (_) => [
        PopupMenuItem(
          value: _ShoppingListAction.showAccountId,
          child: Text(translations.showAccountId),
        ),
        PopupMenuItem(
          value: _ShoppingListAction.share,
          child: Text(translations.share),
        ),
        PopupMenuItem(
          value: _ShoppingListAction.leave,
          child: Text(translations.leave),
        ),
      ],
    );
  }

  Future<void> _showAccountId(BuildContext context, WidgetRef ref) async {
    final accountId = ref.read(signedInAccountProvider)?.id;
    if (accountId == null) {
      return;
    }
    await showDialog<void>(
      context: context,
      builder: (_) => _AccountIdDialog(accountId: accountId),
    );
  }

  Future<void> _share(BuildContext context, WidgetRef ref) async {
    final messenger = ScaffoldMessenger.of(context);
    final sharedMessage = context.t.shoppingList.shared;
    final userId = await showDialog<String>(
      context: context,
      builder: (_) => const _ShareDialog(),
    );
    if (userId == null || userId.isEmpty) {
      return;
    }
    final repository = await ref.read(shoppingListRepositoryProvider.future);
    try {
      await repository.shareList(list, userId);
    } on DioException {
      // apiClientProvider already showed the error dialog.
      return;
    }
    messenger.showSnackBar(SnackBar(content: Text(sharedMessage)));
  }

  Future<void> _leave(BuildContext context, WidgetRef ref) async {
    final accountId = ref.read(signedInAccountProvider)?.id;
    if (accountId == null) {
      return;
    }
    final isConfirmed = await showDialog<bool>(
      context: context,
      builder: (_) => const _LeaveDialog(),
    );
    if (isConfirmed != true) {
      return;
    }
    final repository = await ref.read(shoppingListRepositoryProvider.future);
    try {
      await repository.leaveList(list, accountId);
    } on DioException {
      // apiClientProvider already showed the error dialog.
    }
  }
}

class _AccountIdDialog extends StatelessWidget {
  const _AccountIdDialog({required this.accountId});

  final String accountId;

  @override
  Widget build(BuildContext context) {
    final translations = context.t.shoppingList;
    return AlertDialog(
      title: Text(translations.accountIdTitle),
      content: SelectableText(accountId),
      actions: [
        TextButton(
          onPressed: () async {
            final messenger = ScaffoldMessenger.of(context);
            await Clipboard.setData(ClipboardData(text: accountId));
            messenger.showSnackBar(
              SnackBar(content: Text(translations.accountIdCopied)),
            );
          },
          child: Text(translations.accountIdCopy),
        ),
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(translations.close),
        ),
      ],
    );
  }
}

class _ShareDialog extends HookWidget {
  const _ShareDialog();

  @override
  Widget build(BuildContext context) {
    final translations = context.t.shoppingList;
    final controller = useTextEditingController();
    return AlertDialog(
      title: Text(translations.shareTitle),
      content: TextField(
        controller: controller,
        autofocus: true,
        decoration: InputDecoration(labelText: translations.shareUserId),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(translations.cancel),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(controller.text.trim()),
          child: Text(translations.shareSubmit),
        ),
      ],
    );
  }
}

class _LeaveDialog extends StatelessWidget {
  const _LeaveDialog();

  @override
  Widget build(BuildContext context) {
    final translations = context.t.shoppingList;
    return AlertDialog(
      title: Text(translations.leaveTitle),
      content: Text(translations.leaveMessage),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: Text(translations.cancel),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(translations.leaveConfirm),
        ),
      ],
    );
  }
}
