import 'package:material_ui/material_ui.dart';

import '../theme/app_spacing.dart';
import 'slide_to_confirm.dart';

Future<bool> showSlideToConfirmDialog(
  BuildContext context, {
  required String title,
  required String message,
  required String cancelLabel,
  required String slideLabel,
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(title),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          spacing: AppSpacing.space24,
          children: [
            Text(message),
            SlideToConfirm(
              label: slideLabel,
              onConfirmed: () => Navigator.of(dialogContext).pop(true),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: Text(cancelLabel),
          ),
        ],
      ),
    ) ??
    false;
