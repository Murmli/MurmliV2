import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:hooks_riverpod/misc.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/crash_reporting/api_trace_id_event_processor.dart';

import '../../../helpers/http_client_adapter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';

void main() {
  late RecordingHttpClientAdapter adapter;
  late Dio apiClient;

  Future<DioException> failingRequest(Future<void> Function() send) async {
    try {
      await send();
    } on DioException catch (error) {
      return error;
    }
    throw StateError('The request did not fail.');
  }

  setUp(() async {
    adapter = RecordingHttpClientAdapter(
      respond: (_) async => ResponseBody.fromString('', 500),
    );
    apiClient = createContainer(
      await createPreferences(),
    ).read(apiClientProvider)..httpClientAdapter = adapter;
  });

  group('ApiTraceIdEventProcessor', () {
    test('event of a failed request to the API carries its trace id', () async {
      final error = await failingRequest(
        () => apiClient.get<void>('/v1/example'),
      );

      final event = await ApiTraceIdEventProcessor().apply(
        SentryEvent(throwable: error),
        Hint(),
      );

      expect(event!.tags, {
        'api_trace_id': sentTraceId(adapter.requests.single),
      });
    });

    test('event of a failed request to the API that Riverpod rethrew carries its trace id', () async {
      final error = await failingRequest(
        () => apiClient.get<void>('/v1/example'),
      );
      ProviderException? rethrownError;
      try {
        AsyncValue<void>.error(error, StackTrace.current).requireValue;
      } on ProviderException catch (exception) {
        rethrownError = exception;
      }

      final event = await ApiTraceIdEventProcessor().apply(
        SentryEvent(throwable: rethrownError),
        Hint(),
      );

      expect(event!.tags, {
        'api_trace_id': sentTraceId(adapter.requests.single),
      });
    });

    test(
      'event of a failed request to another origin carries no trace id',
      () async {
        final error = await failingRequest(
          () => apiClient.getUri<void>(Uri.https('example.com', '/data')),
        );

        final event = await ApiTraceIdEventProcessor().apply(
          SentryEvent(throwable: error),
          Hint(),
        );

        expect(event!.tags, isNull);
      },
    );
  });
}
