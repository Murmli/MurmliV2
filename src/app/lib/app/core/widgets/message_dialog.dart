import 'package:material_ui/material_ui.dart';

Future<void> showMessageDialog(
  BuildContext context, {
  required String title,
  required String message,
  required String confirmLabel,
}) => showDialog<void>(
  context: context,
  builder: (dialogContext) => AlertDialog(
    title: Text(title),
    content: Text(message),
    actions: [
      TextButton(
        onPressed: () => Navigator.of(dialogContext).pop(),
        child: Text(confirmLabel),
      ),
    ],
  ),
);
