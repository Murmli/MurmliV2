import 'package:sentry_flutter/sentry_flutter.dart';

import 'api_trace_id_event_processor.dart';

void configureCrashReporting(
  SentryOptions options, {
  required String dsn,
  required String environment,
}) {
  options
    ..dsn = dsn
    ..environment = environment
    ..enablePrintBreadcrumbs = false
    ..recordHttpBreadcrumbs = false
    ..captureFailedRequests = false
    ..sendClientReports = false
    ..tracePropagationTargets.clear()
    ..addEventProcessor(ApiTraceIdEventProcessor());
}
