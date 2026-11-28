import 'package:material_ui/material_ui.dart';

import 'confirmation_dialog_frame.dart';
import 'slide_to_confirm.dart';

Future<bool> showSlideToConfirmDialog(
  BuildContext context, {
  required IconData icon,
  required String title,
  required String message,
  required String cancelLabel,
  required String slideLabel,
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (dialogContext) => ConfirmationDialogFrame(
        icon: icon,
        isDestructive: true,
        title: title,
        message: message,
        cancelLabel: cancelLabel,
        confirmation: SlideToConfirm(
          label: slideLabel,
          onConfirmed: () => Navigator.of(dialogContext).pop(true),
        ),
      ),
    ) ??
    false;
