import 'dart:io';

import 'package:path_provider/path_provider.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

import 'crash_reporting_options.dart';

// Android SDK: sentry, iOS SDK: io.sentry and SentryCrash.
const _nativeSdkCacheDirectoryNames = ['sentry', 'io.sentry', 'SentryCrash'];

class CrashReporter {
  const CrashReporter();

  Future<void> start({required String dsn, required String environment}) =>
      SentryFlutter.init(
        (options) => configureCrashReporting(
          options,
          dsn: dsn,
          environment: environment,
        ),
      );

  Future<void> setUserId(String? id) async {
    await Sentry.configureScope(
      (scope) => scope.setUser(id == null ? null : SentryUser(id: id)),
    );
  }

  Future<void> stop() async {
    final cacheDirectory = await getApplicationCacheDirectory();
    // Closing flushes the native SDKs (iOS sends every cached report), the
    // second deletion removes what they wrote while closing.
    await _deleteNativeSdkCaches(cacheDirectory);
    await Sentry.close();
    await _deleteNativeSdkCaches(cacheDirectory);
  }

  Future<void> _deleteNativeSdkCaches(Directory cacheDirectory) async {
    for (final name in _nativeSdkCacheDirectoryNames) {
      final directory = Directory('${cacheDirectory.path}/$name');
      if (await directory.exists()) {
        await directory.delete(recursive: true);
      }
    }
  }
}
