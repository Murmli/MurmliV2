import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/router/app_router.dart';

enum HomeTab {
  recipes(icon: Icons.restaurant_menu, route: RecipesRoute()),
  planner(icon: Icons.calendar_today_outlined, route: PlannerRoute()),
  chat(icon: Icons.auto_awesome, route: ChatRoute()),
  shoppingList(icon: Icons.shopping_cart_outlined, route: ShoppingListRoute()),
  calories(icon: Icons.local_fire_department_outlined, route: CaloriesRoute());

  const HomeTab({required this.icon, required this.route});

  final IconData icon;
  final PageRouteInfo route;

  String label(Translations translations) => switch (this) {
    HomeTab.recipes => translations.home.tabs.recipes.label,
    HomeTab.planner => translations.home.tabs.planner.label,
    HomeTab.chat => translations.home.tabs.chat.label,
    HomeTab.shoppingList => translations.home.tabs.shoppingList.label,
    HomeTab.calories => translations.home.tabs.calories.label,
  };

  String title(Translations translations) => switch (this) {
    HomeTab.recipes => translations.home.tabs.recipes.title,
    HomeTab.planner => translations.home.tabs.planner.title,
    HomeTab.chat => translations.home.tabs.chat.title,
    HomeTab.shoppingList => translations.home.tabs.shoppingList.title,
    HomeTab.calories => translations.home.tabs.calories.title,
  };
}
