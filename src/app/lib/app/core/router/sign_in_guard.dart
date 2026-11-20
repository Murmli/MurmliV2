import 'dart:async';

import 'package:auto_route/auto_route.dart';

import 'app_router.dart';

class SignInGuard extends AutoRouteGuard {
  SignInGuard({required this.isSignedIn});

  final bool Function() isSignedIn;

  @override
  void onNavigation(NavigationResolver resolver, StackRouter router) {
    if (isSignedIn()) {
      resolver.next();
      return;
    }
    unawaited(router.replaceAll([const OnboardingRoute()]));
    resolver.next(false);
  }
}
