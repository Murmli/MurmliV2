import 'package:material_ui/material_ui.dart';

import 'confirmation_dialog_frame.dart';

Future<bool> showConfirmationDialog(
  BuildContext context, {
  required IconData icon,
  required String title,
  required String message,
  required String cancelLabel,
  required String confirmLabel,
  bool isDestructive = false,
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (dialogContext) => ConfirmationDialogFrame(
        icon: icon,
        isDestructive: isDestructive,
        title: title,
        message: message,
        cancelLabel: cancelLabel,
        confirmation: FilledButton(
          onPressed: () => Navigator.of(dialogContext).pop(true),
          child: Text(confirmLabel),
        ),
      ),
    ) ??
    false;
