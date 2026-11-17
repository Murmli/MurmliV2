import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/crash_reporting/crash_reporter_provider.dart';
import 'package:app/app/core/crash_reporting/crash_reporting_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/storage/preference_keys.dart';

import '../../../helpers/crash_reporter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';

void main() {
  RecordingCrashReporter crashReporterOf(ProviderContainer container) =>
      container.read(crashReporterProvider) as RecordingCrashReporter;

  group('CrashReporting', () {
    for (final flavor in [Flavor.development, Flavor.production]) {
      test(
        'start without stored choice in ${flavor.name} starts crash reporting with the flavor as environment',
        () async {
          final container = createContainer(
            await createPreferences(),
            flavor: flavor,
            debugBuild: false,
          );

          await container.read(crashReportingProvider.notifier).start();

          expect(crashReporterOf(container).calls, ['start ${flavor.name}']);
        },
      );
    }

    test('start in local starts no crash reporting', () async {
      final container = createContainer(
        await createPreferences(),
        flavor: Flavor.local,
        debugBuild: false,
      );

      await container.read(crashReportingProvider.notifier).start();

      expect(crashReporterOf(container).calls, isEmpty);
    });

    test('start in a debug build starts no crash reporting', () async {
      final container = createContainer(
        await createPreferences(),
        flavor: Flavor.development,
        debugBuild: true,
      );

      await container.read(crashReportingProvider.notifier).start();

      expect(crashReporterOf(container).calls, isEmpty);
    });

    test('setEnabled false persists the choice and stops crash reporting, so the next app start reports nothing', () async {
      final container = createContainer(await createPreferences());

      await container.read(crashReportingProvider.notifier).setEnabled(false);

      expect(container.read(crashReportingProvider), isFalse);
      expect(crashReporterOf(container).calls, ['stop']);
      final restartedContainer = createContainer(
        await reopenPreferences(),
        flavor: Flavor.development,
        debugBuild: false,
      );
      await restartedContainer.read(crashReportingProvider.notifier).start();
      expect(crashReporterOf(restartedContainer).calls, isEmpty);
    });

    test(
      'setEnabled true after switching off starts crash reporting again',
      () async {
        final container = createContainer(
          await createPreferences({
            PreferenceKeys.crashReportingEnabled: false,
          }),
          flavor: Flavor.development,
          debugBuild: false,
        );

        await container.read(crashReportingProvider.notifier).setEnabled(true);

        expect(container.read(crashReportingProvider), isTrue);
        expect(crashReporterOf(container).calls, ['start development']);
      },
    );

    test('setEnabled true while stopping starts crash reporting only after the stop finished', () async {
      final container = createContainer(
        await createPreferences(),
        flavor: Flavor.development,
        debugBuild: false,
      );
      final crashReporter = crashReporterOf(container)
        ..pendingStop = Completer();
      final notifier = container.read(crashReportingProvider.notifier);

      final switchedOff = notifier.setEnabled(false);
      final switchedOn = notifier.setEnabled(true);
      await pumpEventQueue();

      expect(crashReporter.calls, ['stop']);
      crashReporter.pendingStop!.complete();
      await Future.wait([switchedOff, switchedOn]);
      expect(crashReporter.calls, ['stop', 'start development']);
    });
  });
}
