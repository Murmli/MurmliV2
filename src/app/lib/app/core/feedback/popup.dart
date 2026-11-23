import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../theme/app_spacing.dart';

Future<bool> showConfirmationPopup(
  BuildContext context, {
  required String title,
  required String message,
  required String cancelLabel,
  required String confirmLabel,
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: Text(cancelLabel),
          ),
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: Text(confirmLabel),
          ),
        ],
      ),
    ) ??
    false;

Future<void> showMessagePopup(
  BuildContext context, {
  required String title,
  required String message,
  String? supportCode,
}) => showDialog<void>(
  context: context,
  builder: (_) =>
      _MessagePopup(title: title, message: message, supportCode: supportCode),
);

class _MessagePopup extends StatelessWidget {
  const _MessagePopup({
    required this.title,
    required this.message,
    required this.supportCode,
  });

  final String title;
  final String message;
  final String? supportCode;

  @override
  Widget build(BuildContext context) {
    final supportCode = this.supportCode;
    return AlertDialog(
      title: Text(title),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(message),
          if (supportCode != null) _SupportCode(supportCode),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(context.t.requestFailure.close),
        ),
      ],
    );
  }
}

class _SupportCode extends StatelessWidget {
  const _SupportCode(this.supportCode);

  final String supportCode;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      const SizedBox(height: AppSpacing.space12),
      Text(context.t.requestFailure.supportCode),
      const SizedBox(height: AppSpacing.space4),
      SelectableText(supportCode, style: TextTheme.of(context).titleSmall),
    ],
  );
}
