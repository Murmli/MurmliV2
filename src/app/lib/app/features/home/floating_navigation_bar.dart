import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/theme/app_colors.dart';
import '../../core/theme/app_motion.dart';
import '../../core/theme/app_spacing.dart';
import '../../core/theme/app_typography.dart';
import 'home_tab.dart';

class FloatingNavigationBar extends StatelessWidget {
  const FloatingNavigationBar({
    required this.activeTab,
    required this.onTabSelected,
    super.key,
  });

  final HomeTab activeTab;
  final ValueChanged<HomeTab> onTabSelected;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return SafeArea(
      top: false,
      minimum: const EdgeInsets.only(bottom: AppSpacing.space20),
      child: Padding(
        padding: const EdgeInsetsDirectional.fromSTEB(
          AppSpacing.space16,
          AppSpacing.space16,
          AppSpacing.space16,
          0,
        ),
        child: Container(
          constraints: const BoxConstraints(minHeight: 68),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.space6),
          decoration: BoxDecoration(
            color: colors.surface,
            borderRadius: BorderRadius.circular(AppRadius.navigationBar),
            border: Border.all(color: colors.line),
            boxShadow: colors.navigationShadow,
          ),
          child: Material(
            type: MaterialType.transparency,
            child: Row(
              spacing: AppSpacing.space2,
              children: [
                for (final tab in HomeTab.values)
                  if (tab == HomeTab.chat)
                    _ChatTabButton(
                      isActive: tab == activeTab,
                      onPressed: () => onTabSelected(tab),
                    )
                  else
                    Expanded(
                      child: _TabButton(
                        tab: tab,
                        isActive: tab == activeTab,
                        onPressed: () => onTabSelected(tab),
                      ),
                    ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _TabButton extends StatelessWidget {
  const _TabButton({
    required this.tab,
    required this.isActive,
    required this.onPressed,
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
            spacing: AppSpacing.space3,
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

class _ChatTabButton extends StatelessWidget {
  const _ChatTabButton({required this.isActive, required this.onPressed});

  final bool isActive;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Semantics(
      button: true,
      selected: isActive,
      label: HomeTab.chat.label(context.t),
      excludeSemantics: true,
      onTap: onPressed,
      child: InkWell(
        onTap: onPressed,
        borderRadius: BorderRadius.circular(AppRadius.navigationTab),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minWidth: 72, minHeight: 60),
          child: Center(
            heightFactor: 1,
            child: Transform.translate(
              offset: const Offset(0, -9),
              child: AnimatedContainer(
                duration: AppMotion.short(context),
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  color: colors.primary,
                  shape: BoxShape.circle,
                  boxShadow: [
                    BoxShadow(
                      color: colors.primary.withValues(alpha: 0.55),
                      offset: const Offset(0, 10),
                      blurRadius: 20,
                      spreadRadius: -6,
                    ),
                    BoxShadow(
                      color: isActive ? colors.primarySoft : Colors.transparent,
                      spreadRadius: 4,
                    ),
                  ],
                ),
                child: Icon(
                  HomeTab.chat.icon,
                  size: 26,
                  color: colors.onPrimary,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
