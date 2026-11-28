import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import 'home_tab.dart';
import 'widgets/floating_navigation_bar.dart';
import 'widgets/home_header.dart';

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
                if (!activeTab.rendersOwnHeader)
                  HomeHeader(title: activeTab.title(context.t)),
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
