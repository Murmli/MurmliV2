import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/crash_reporting/crash_reporting_options.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/http_client_adapter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';
import '../../../helpers/sentry.dart';

void main() {
  group('configureCrashReporting', () {
    test('reported error of a failed request to the API carries its trace id as tag', () async {
      final envelopes = await startRecordingSentry(
        configure: (options) => configureCrashReporting(
          options,
          dsn: recordingSentryDsn,
          environment: 'development',
        ),
      );
      final adapter = RecordingHttpClientAdapter(
        respond: (_) async => ResponseBody.fromString('', 500),
      );
      final container = createContainer(await createPreferences());
      await signIn(container);
      final apiClient = container.read(apiClientProvider)
        ..httpClientAdapter = adapter;

      try {
        await apiClient.get<void>('/v1/example');
      } on DioException catch (error, stackTrace) {
        await Sentry.captureException(error, stackTrace: stackTrace);
      }

      final event =
          envelopes.single.items.single.originalObject! as SentryEvent;
      expect(
        event.tags,
        containsPair('api_trace_id', sentTraceId(adapter.requests.single)),
      );
    });
  });
}
