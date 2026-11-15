import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/theme/app_typography.dart';
import '../home_tab.dart';

class NavigationTabButton extends StatelessWidget {
  const NavigationTabButton({
    required this.tab,
    required this.isActive,
    required this.onPressed,
    super.key,
  });

  final HomeTab tab;
  final bool isActive;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final foregroundColor = isActive ? colors.primaryText : colors.inkTertiary;
    final label = tab.label(context.t);
    return Semantics(
      button: true,
      selected: isActive,
      label: label,
      excludeSemantics: true,
      onTap: onPressed,
      child: InkWell(
        onTap: onPressed,
        borderRadius: BorderRadius.circular(AppRadius.navigationTab),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 60),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            mainAxisAlignment: MainAxisAlignment.center,
            spacing: AppSpacing.space4,
            children: [
              AnimatedContainer(
                duration: AppMotion.short(context),
                width: 52,
                height: 30,
                decoration: BoxDecoration(
                  color: isActive ? colors.primarySoft : Colors.transparent,
                  borderRadius: BorderRadius.circular(
                    AppRadius.navigationIndicator,
                  ),
                ),
                child: Icon(tab.icon, size: 22, color: foregroundColor),
              ),
              Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.caption.copyWith(
                  fontWeight: FontWeight.w700,
                  color: foregroundColor,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
