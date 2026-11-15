import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../home_tab.dart';
import 'chat_tab_button.dart';
import 'navigation_tab_button.dart';

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
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.space8),
          decoration: BoxDecoration(
            color: colors.surface,
            borderRadius: BorderRadius.circular(AppRadius.navigationBar),
            border: Border.all(color: colors.line),
            boxShadow: colors.navigationShadow,
          ),
          child: Material(
            type: MaterialType.transparency,
            child: Row(
              spacing: AppSpacing.space4,
              children: [
                for (final tab in HomeTab.values)
                  if (tab == HomeTab.chat)
                    ChatTabButton(
                      isActive: tab == activeTab,
                      onPressed: () => onTabSelected(tab),
                    )
                  else
                    Expanded(
                      child: NavigationTabButton(
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
