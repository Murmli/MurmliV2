import 'package:material_ui/material_ui.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

class ConfirmationDialogFrame extends StatelessWidget {
  const ConfirmationDialogFrame({
    required this.icon,
    required this.isDestructive,
    required this.title,
    required this.message,
    required this.confirmation,
    required this.cancelLabel,
    super.key,
  });

  final IconData icon;
  final bool isDestructive;
  final String title;
  final String message;
  final Widget confirmation;
  final String cancelLabel;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final theme = Theme.of(context);
    final textTheme = theme.textTheme;
    final buttonTextStyle = textTheme.bodyMedium!.copyWith(
      fontWeight: FontWeight.w700,
    );
    const buttonSize = Size.fromHeight(48);
    return Dialog(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(AppSpacing.space24),
        child: Semantics(
          scopesRoute: true,
          explicitChildNodes: true,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            spacing: AppSpacing.space12,
            children: [
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: _DialogIconTile(
                  icon: icon,
                  isDestructive: isDestructive,
                ),
              ),
              Semantics(
                header: true,
                namesRoute: true,
                child: Text(
                  title,
                  style: textTheme.headlineSmall!.copyWith(
                    fontSize: 22,
                    height: 28 / 22,
                    fontVariations: const [FontVariation.opticalSize(22)],
                  ),
                ),
              ),
              Text(
                message,
                style: textTheme.bodyMedium!.copyWith(
                  color: colors.inkSecondary,
                ),
              ),
              Padding(
                padding: const EdgeInsetsDirectional.only(
                  top: AppSpacing.space8,
                ),
                child: FilledButtonTheme(
                  data: FilledButtonThemeData(
                    style: FilledButton.styleFrom(
                      minimumSize: buttonSize,
                      textStyle: buttonTextStyle,
                      backgroundColor: isDestructive
                          ? colors.categoryRed
                          : null,
                      foregroundColor: isDestructive ? colors.surface : null,
                    ).merge(theme.filledButtonTheme.style),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    spacing: AppSpacing.space8,
                    children: [
                      confirmation,
                      OutlinedButton(
                        style: OutlinedButton.styleFrom(
                          minimumSize: buttonSize,
                          textStyle: buttonTextStyle,
                        ),
                        onPressed: () => Navigator.of(context).pop(false),
                        child: Text(cancelLabel),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _DialogIconTile extends StatelessWidget {
  const _DialogIconTile({required this.icon, required this.isDestructive});

  final IconData icon;
  final bool isDestructive;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Container(
      width: 48,
      height: 48,
      decoration: BoxDecoration(
        color: isDestructive ? colors.categoryRedSoft : colors.primarySoft,
        borderRadius: BorderRadius.circular(16),
      ),
      child: Icon(
        icon,
        size: 24,
        color: isDestructive ? colors.categoryRed : colors.primaryText,
      ),
    );
  }
}
