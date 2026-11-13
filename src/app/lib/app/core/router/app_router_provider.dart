import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../features/onboarding/onboarding_completed_provider.dart';
import 'app_router.dart';
import 'onboarding_guard.dart';

part 'app_router_provider.g.dart';

@Riverpod(keepAlive: true)
AppRouter appRouter(Ref ref) => AppRouter(
  onboardingGuard: OnboardingGuard(
    isOnboardingCompleted: () => ref.read(onboardingCompletedProvider),
  ),
);
