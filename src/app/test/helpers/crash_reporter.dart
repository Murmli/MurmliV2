import 'dart:async';

import 'package:app/app/core/crash_reporting/crash_reporter.dart';

class RecordingCrashReporter implements CrashReporter {
  final calls = <String>[];
  Completer<void>? pendingStop;

  @override
  Future<void> start({
    required String dsn,
    required String environment,
  }) async => calls.add('start $environment');

  @override
  Future<void> setUserId(String? id) async => calls.add('user ${id ?? 'none'}');

  @override
  Future<void> stop() async {
    calls.add('stop');
    await pendingStop?.future;
  }
}
