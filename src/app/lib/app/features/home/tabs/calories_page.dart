import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../home_tab.dart';
import 'tab_placeholder.dart';

@RoutePage()
class CaloriesPage extends StatelessWidget {
  const CaloriesPage({super.key});

  @override
  Widget build(BuildContext context) =>
      const TabPlaceholder(tab: HomeTab.calories);
}
