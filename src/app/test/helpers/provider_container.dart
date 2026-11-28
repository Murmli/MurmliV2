import 'dart:async';

import 'package:clock/clock.dart';
import 'package:drift/native.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:hooks_riverpod/misc.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/build_mode/debug_build_provider.dart';
import 'package:app/app/core/crash_reporting/crash_reporter_provider.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/database/app_database_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/flavor/flavor_provider.dart';
import 'package:app/app/core/logging/talker_provider.dart';
import 'package:app/app/core/network/network_available_provider.dart';
import 'package:app/app/core/storage/shared_preferences_provider.dart';
import 'package:app/app/core/time/clock_provider.dart';

import 'crash_reporter.dart';

ProviderContainer createContainer(
  SharedPreferencesWithCache preferences, {
  Flavor? flavor,
  bool? debugBuild,
  Account? signedInAccount,
  Clock? clock,
  Map<String, String>? secureStorage,
  AppDatabase? database,
  List<Override> overrides = const [],
}) {
  FlutterSecureStorage.setMockInitialValues(secureStorage ?? {});
  return ProviderContainer.test(
    overrides: [
      sharedPreferencesProvider.overrideWithValue(preferences),
      talkerProvider.overrideWithValue(
        Talker(settings: TalkerSettings(useConsoleLogs: false)),
      ),
      crashReporterProvider.overrideWithValue(RecordingCrashReporter()),
      networkAvailableProvider.overrideWithBuild((ref, notifier) => true),
      if (flavor != null) flavorProvider.overrideWithValue(flavor),
      if (debugBuild != null) debugBuildProvider.overrideWithValue(debugBuild),
      if (signedInAccount != null)
        signedInAccountProvider.overrideWithBuild(
          (ref, notifier) => signedInAccount,
        ),
      if (clock != null) appClockProvider.overrideWithValue(clock),
      if (database != null)
        appDatabaseProvider.overrideWithValue(database)
      else
        appDatabaseProvider.overrideWith((ref) {
          final inMemoryDatabase = AppDatabase(NativeDatabase.memory());
          ref.onDispose(() => unawaited(inMemoryDatabase.close()));
          return inMemoryDatabase;
        }),
      ...overrides,
    ],
  );
}
