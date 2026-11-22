import 'package:clock/clock.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/build_mode/debug_build_provider.dart';
import 'package:app/app/core/crash_reporting/crash_reporter_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/flavor/flavor_provider.dart';
import 'package:app/app/core/logging/talker_provider.dart';
import 'package:app/app/core/storage/shared_preferences_provider.dart';
import 'package:app/app/core/time/clock_provider.dart';

import 'crash_reporter.dart';

ProviderContainer createContainer(
  SharedPreferencesWithCache preferences, {
  Flavor? flavor,
  bool? debugBuild,
  Account? signedInAccount,
  Clock? clock,
}) => ProviderContainer.test(
  overrides: [
    sharedPreferencesProvider.overrideWithValue(preferences),
    talkerProvider.overrideWithValue(
      Talker(settings: TalkerSettings(useConsoleLogs: false)),
    ),
    crashReporterProvider.overrideWithValue(RecordingCrashReporter()),
    if (flavor != null) flavorProvider.overrideWithValue(flavor),
    if (debugBuild != null) debugBuildProvider.overrideWithValue(debugBuild),
    if (signedInAccount != null)
      signedInAccountProvider.overrideWithBuild(
        (ref, notifier) => signedInAccount,
      ),
    if (clock != null) appClockProvider.overrideWithValue(clock),
  ],
);
