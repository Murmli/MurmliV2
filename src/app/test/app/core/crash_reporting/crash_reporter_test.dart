import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:path_provider_platform_interface/path_provider_platform_interface.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

import 'package:app/app/core/crash_reporting/crash_reporter.dart';

import '../../../helpers/sentry.dart';

void main() {
  late Directory cacheDirectory;

  setUp(() async {
    cacheDirectory = await Directory.systemTemp.createTemp();
    addTearDown(() => cacheDirectory.delete(recursive: true));
    PathProviderPlatform.instance = _FakePathProviderPlatform(
      cacheDirectory.path,
    );
    for (final path in [
      'sentry/outbox/report.envelope',
      'io.sentry/dsn-hash/envelopes/report.envelope',
      'SentryCrash/Runner/Reports/report.json',
      'other/file',
    ]) {
      await File('${cacheDirectory.path}/$path').create(recursive: true);
    }
  });

  group('CrashReporter', () {
    test('stop ends reporting and deletes the reports cached by the native SDKs but no other cache', () async {
      final envelopes = await startRecordingSentry();

      await const CrashReporter().stop();
      await Sentry.captureException(StateError('after stop'));

      expect(envelopes, isEmpty);
      expect(_filesIn(cacheDirectory), ['other/file']);
    });

    test('stop deletes the cached reports before the native SDKs flush them while closing and the reports written while closing', () async {
      final nativeSdk = _FakeNativeSdk(cacheDirectory);
      await startRecordingSentry(
        configure: (options) => options.addIntegration(nativeSdk),
      );

      await const CrashReporter().stop();

      expect(nativeSdk.cachedFilesWhenClosing, ['other/file']);
      expect(_filesIn(cacheDirectory), ['other/file']);
    });
  });
}

List<String> _filesIn(Directory directory) => directory
    .listSync(recursive: true)
    .whereType<File>()
    .map((file) => file.path.substring(directory.path.length + 1))
    .toList();

class _FakePathProviderPlatform extends PathProviderPlatform {
  _FakePathProviderPlatform(this.applicationCachePath);

  final String applicationCachePath;

  @override
  Future<String?> getApplicationCachePath() async => applicationCachePath;
}

class _FakeNativeSdk extends Integration<SentryOptions> {
  _FakeNativeSdk(this.cacheDirectory);

  final Directory cacheDirectory;
  List<String>? cachedFilesWhenClosing;

  @override
  void call(Hub hub, SentryOptions options) {}

  @override
  Future<void> close() async {
    cachedFilesWhenClosing = _filesIn(cacheDirectory);
    await File(
      '${cacheDirectory.path}/io.sentry/dsn-hash/envelopes/session.envelope',
    ).create(recursive: true);
  }
}
