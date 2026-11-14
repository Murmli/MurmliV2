import 'package:auto_route/auto_route.dart';

import 'app_router.gr.dart';

export 'app_router.gr.dart';

@AutoRouterConfig()
class AppRouter extends RootStackRouter {
  @override
  List<AutoRoute> get routes => [
    AutoRoute(
      page: HomeRoute.page,
      initial: true,
      children: [
        AutoRoute(page: RecipesRoute.page),
        AutoRoute(page: PlannerRoute.page),
        AutoRoute(page: ChatRoute.page, initial: true),
        AutoRoute(page: ShoppingListRoute.page),
        AutoRoute(page: CaloriesRoute.page),
      ],
    ),
  ];
}
