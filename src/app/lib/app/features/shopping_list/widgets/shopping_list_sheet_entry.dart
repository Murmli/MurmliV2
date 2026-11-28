import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

/// An entry without [onTap] is shown and announced as disabled.
class ShoppingListSheetEntry extends StatelessWidget {
  const ShoppingListSheetEntry({
    required this.icon,
    required this.label,
    this.subtitle,
    this.isDestructive = false,
    this.onTap,
    this.trailing,
    this.semanticsLabel,
    this.semanticsValue,
    this.isExpanded,
    super.key,
  });

  final IconData icon;
  final String label;
  final String? subtitle;
  final bool isDestructive;
  final VoidCallback? onTap;
  final Widget? trailing;
  final String? semanticsLabel;
  final String? semanticsValue;

  /// Non-null marks the entry as an expander in the given state.
  final bool? isExpanded;

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
      value: semanticsValue ?? subtitle,
      expanded: isExpanded,
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
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        label,
                        style: textTheme.bodyMedium!.copyWith(
                          fontWeight: FontWeight.w600,
                          color: labelColor,
                        ),
                      ),
                      if (subtitle case final subtitle?)
                        Text(
                          subtitle,
                          style: textTheme.labelSmall!.copyWith(
                            fontSize: 12,
                            height: 16 / 12,
                            fontWeight: FontWeight.w600,
                            color: colors.inkSecondary,
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                    ],
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
