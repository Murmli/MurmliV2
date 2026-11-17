import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'crash_reporter.dart';

part 'crash_reporter_provider.g.dart';

@Riverpod(keepAlive: true)
CrashReporter crashReporter(Ref ref) => const CrashReporter();
