import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/features/onboarding/onboarding_completed_provider.dart';

import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';

void main() {
  group('OnboardingCompleted', () {
    test('build without stored flag returns false', () async {
      final container = createContainer(await createPreferences());

      expect(container.read(onboardingCompletedProvider), isFalse);
    });

    test(
      'complete persists the flag so a new container starts completed',
      () async {
        final container = createContainer(await createPreferences());

        await container.read(onboardingCompletedProvider.notifier).complete();

        expect(container.read(onboardingCompletedProvider), isTrue);
        final restartedContainer = createContainer(await reopenPreferences());
        expect(restartedContainer.read(onboardingCompletedProvider), isTrue);
      },
    );
  });
}
