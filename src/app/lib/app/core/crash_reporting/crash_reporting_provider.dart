import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../build_mode/debug_build_provider.dart';
import '../flavor/flavor.dart';
import '../flavor/flavor_provider.dart';
import '../storage/preference_keys.dart';
import '../storage/shared_preferences_provider.dart';
import 'crash_reporter_provider.dart';

part 'crash_reporting_provider.g.dart';

const _appProjectDsn =
    'https://93e37909702ace86ccf3e099e7675bee@o4512447711412224.ingest.de.sentry.io/4512447746474064';

@Riverpod(keepAlive: true)
class CrashReporting extends _$CrashReporting {
  Future<void> _reporterChange = Future.value();

  @override
  bool build() =>
      ref
          .watch(sharedPreferencesProvider)
          .getBool(PreferenceKeys.crashReportingEnabled) ??
      true;

  Future<void> start() async {
    if (state) {
      await _changeReporter(isEnabled: true);
    }
  }

  Future<void> setEnabled(bool isEnabled) async {
    await ref
        .read(sharedPreferencesProvider)
        .setBool(PreferenceKeys.crashReportingEnabled, isEnabled);
    state = isEnabled;
    await _changeReporter(isEnabled: isEnabled);
  }

  // Waits for the previous change, so switching off and on again quickly
  // cannot delete the cache of the restarted SDK.
  Future<void> _changeReporter({required bool isEnabled}) {
    final change = _reporterChange
        .catchError((Object _) {})
        .then(
          (_) => isEnabled
              ? _startReporter()
              : ref.read(crashReporterProvider).stop(),
        );
    _reporterChange = change;
    return change;
  }

  Future<void> _startReporter() async {
    final flavor = ref.read(flavorProvider);
    final dsn = switch (flavor) {
      Flavor.local => null,
      Flavor.development || Flavor.production => _appProjectDsn,
    };
    if (dsn != null && !ref.read(debugBuildProvider)) {
      await ref
          .read(crashReporterProvider)
          .start(dsn: dsn, environment: flavor.name);
    }
  }
}
