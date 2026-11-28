import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

/// An entry without [onTap] is shown and announced as disabled.
class ShoppingListSheetEntry extends StatelessWidget {
  const ShoppingListSheetEntry({
    required this.icon,
    required this.label,
    this.isDestructive = false,
    this.onTap,
    this.trailing,
    this.semanticsLabel,
    this.semanticsValue,
    super.key,
  });

  final IconData icon;
  final String label;
  final bool isDestructive;
  final VoidCallback? onTap;
  final Widget? trailing;
  final String? semanticsLabel;
  final String? semanticsValue;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final isDisabled = onTap == null;
    final labelColor = isDestructive
        ? colors.categoryRed
        : isDisabled
        ? colors.inkTertiary
        : colors.ink;
    return Semantics(
      container: true,
      button: true,
      enabled: !isDisabled,
      label: semanticsLabel ?? label,
      value: semanticsValue,
      onTap: onTap,
      excludeSemantics: true,
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 56),
            child: Row(
              spacing: 14,
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: BoxDecoration(
                    color: isDestructive
                        ? colors.categoryRedSoft
                        : colors.surfaceSecondary,
                    borderRadius: BorderRadius.circular(AppRadius.iconTile),
                  ),
                  child: Icon(
                    icon,
                    size: 20,
                    color: isDestructive
                        ? colors.categoryRed
                        : colors.inkSecondary,
                  ),
                ),
                Expanded(
                  child: Text(
                    label,
                    style: textTheme.bodyMedium!.copyWith(
                      fontWeight: FontWeight.w600,
                      color: labelColor,
                    ),
                  ),
                ),
                ?trailing,
              ],
            ),
          ),
        ),
      ),
    );
  }
}
