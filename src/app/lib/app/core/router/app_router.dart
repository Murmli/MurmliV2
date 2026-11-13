import 'package:auto_route/auto_route.dart';

import 'app_router.gr.dart';
import 'onboarding_guard.dart';

export 'app_router.gr.dart';

@AutoRouterConfig()
class AppRouter extends RootStackRouter {
  AppRouter({required this.onboardingGuard});

  final OnboardingGuard onboardingGuard;

  @override
  List<AutoRoute> get routes => [
    AutoRoute(page: OnboardingRoute.page),
    AutoRoute(
      page: HomeRoute.page,
      initial: true,
      guards: [onboardingGuard],
      children: [
        AutoRoute(page: RecipesRoute.page),
        AutoRoute(page: PlannerRoute.page),
        AutoRoute(page: ChatRoute.page, initial: true),
        AutoRoute(page: ShoppingListRoute.page),
        AutoRoute(page: CaloriesRoute.page),
      ],
    ),
    AutoRoute(page: SettingsRoute.page),
  ];
}
