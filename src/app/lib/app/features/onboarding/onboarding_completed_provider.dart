import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../core/storage/preference_keys.dart';
import '../../core/storage/shared_preferences_provider.dart';

part 'onboarding_completed_provider.g.dart';

@Riverpod(keepAlive: true)
class OnboardingCompleted extends _$OnboardingCompleted {
  @override
  bool build() =>
      ref
          .watch(sharedPreferencesProvider)
          .getBool(PreferenceKeys.onboardingCompleted) ??
      false;

  Future<void> complete() async {
    await ref
        .read(sharedPreferencesProvider)
        .setBool(PreferenceKeys.onboardingCompleted, true);
    state = true;
  }
}
