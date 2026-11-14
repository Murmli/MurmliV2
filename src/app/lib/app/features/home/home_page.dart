import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/router/app_router.dart';
import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';
import '../../core/theme/app_typography.dart';
import 'floating_navigation_bar.dart';
import 'home_tab.dart';

@RoutePage()
class HomePage extends StatelessWidget {
  const HomePage({super.key});

  @override
  Widget build(BuildContext context) {
    return AutoTabsRouter(
      routes: [for (final tab in HomeTab.values) tab.route],
      builder: (context, child) {
        final tabsRouter = AutoTabsRouter.of(context);
        final activeTab = HomeTab.values[tabsRouter.activeIndex];
        return Scaffold(
          extendBody: true,
          body: SafeArea(
            bottom: false,
            child: Column(
              children: [
                _HomeHeader(title: activeTab.title(context.t)),
                Expanded(child: child),
              ],
            ),
          ),
          bottomNavigationBar: FloatingNavigationBar(
            activeTab: activeTab,
            onTabSelected: (tab) => tabsRouter.setActiveIndex(tab.index),
          ),
        );
      },
    );
  }
}

class _HomeHeader extends StatelessWidget {
  const _HomeHeader({required this.title});

  final String title;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Padding(
      padding: const EdgeInsetsDirectional.fromSTEB(
        AppSpacing.screenMargin,
        AppSpacing.space8,
        AppSpacing.space8,
        AppSpacing.space8,
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              title,
              style: AppTypography.title.copyWith(color: colors.ink),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          IconButton(
            tooltip: context.t.home.settingsTooltip,
            icon: Icon(Icons.settings_outlined, color: colors.inkSecondary),
            onPressed: () =>
                unawaited(context.router.push(const SettingsRoute())),
          ),
        ],
      ),
    );
  }
}
