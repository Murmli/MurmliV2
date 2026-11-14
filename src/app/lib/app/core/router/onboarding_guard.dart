import 'dart:async';

import 'package:auto_route/auto_route.dart';

import 'app_router.dart';

class OnboardingGuard extends AutoRouteGuard {
  OnboardingGuard({required this.isOnboardingCompleted});

  final bool Function() isOnboardingCompleted;

  @override
  void onNavigation(NavigationResolver resolver, StackRouter router) {
    if (isOnboardingCompleted()) {
      resolver.next();
      return;
    }
    unawaited(router.replaceAll([const OnboardingRoute()]));
    resolver.next(false);
  }
}
