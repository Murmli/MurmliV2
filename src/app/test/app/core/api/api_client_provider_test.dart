import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/feedback/reported_request_failure_provider.dart';
import 'package:app/app/core/feedback/request_failure.dart';
import 'package:app/app/core/flavor/app_endpoints.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/logging/talker_provider.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/http_client_adapter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';
import '../../../helpers/sentry.dart';

void main() {
  late ProviderContainer container;

  Future<Dio> createApiClient(
    RecordingHttpClientAdapter adapter, {
    Flavor? flavor,
  }) async {
    container = createContainer(await createPreferences(), flavor: flavor);
    await signIn(container);
    final apiClient = container.read(apiClientProvider)
      ..httpClientAdapter = adapter;
    return apiClient;
  }

  Future<ResponseBody> respondWithServerError(RequestOptions request) async =>
      ResponseBody.fromString('', 500);

  String sentParentId(RequestOptions request) =>
      (request.headers['traceparent'] as String).split('-')[2];

  Future<Response<void>> postWithQueryHeaderAndBody(Dio apiClient) =>
      apiClient.post<void>(
        '/v1/example',
        queryParameters: {'search': 'apple'},
        data: {'weight': 80},
        options: Options(headers: {'X-Example': 'header-value'}),
      );

  final containsQueryHeaderOrBody = anyOf(
    contains('apple'),
    contains('weight'),
    contains('header-value'),
    contains('traceparent'),
  );

  group('apiClient', () {
    for (final flavor in [Flavor.local, Flavor.development]) {
      test('request in ${flavor.name} goes to the API of the flavor', () async {
        final adapter = RecordingHttpClientAdapter();
        final apiClient = await createApiClient(adapter, flavor: flavor);

        await apiClient.get<void>('/v1/example');

        expect(
          adapter.requests.single.uri,
          AppEndpoints.of(
            flavor,
            defaultTargetPlatform,
          ).api.resolve('/v1/example'),
        );
      });
    }

    test('request to the API carries a W3C traceparent with version 00, non-zero ids and flag 01', () async {
      final adapter = RecordingHttpClientAdapter();
      final apiClient = await createApiClient(adapter);

      await apiClient.get<void>('/v1/example');

      final traceParent =
          adapter.requests.single.headers['traceparent'] as String;
      expect(traceParent, matches(r'^00-[0-9a-f]{32}-[0-9a-f]{16}-01$'));
      final [_, traceId, parentId, _] = traceParent.split('-');
      expect(traceId, isNot('0' * 32));
      expect(parentId, isNot('0' * 16));
    });

    test(
      'every request to the API carries a new trace id and parent id',
      () async {
        final adapter = RecordingHttpClientAdapter();
        final apiClient = await createApiClient(adapter);

        await apiClient.get<void>('/v1/example');
        await apiClient.get<void>('/v1/example');

        expect(
          sentTraceId(adapter.requests.first),
          isNot(sentTraceId(adapter.requests.last)),
        );
        expect(
          sentParentId(adapter.requests.first),
          isNot(sentParentId(adapter.requests.last)),
        );
      },
    );

    test('request to the API carries its traceparent but no tracing header of Sentry while Sentry reports', () async {
      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      addTearDown(() => server.close(force: true));
      final receivedHeaderNames = <String>[];
      server.listen((request) async {
        request.headers.forEach((name, _) => receivedHeaderNames.add(name));
        request.response.statusCode = HttpStatus.noContent;
        await request.response.close();
      });
      final previousHttpOverrides = HttpOverrides.current;
      HttpOverrides.global = _ProxyHttpOverrides(server.port);
      addTearDown(() => HttpOverrides.global = previousHttpOverrides);
      await startRecordingSentry();
      container = createContainer(
        await createPreferences(),
        flavor: Flavor.local,
      );
      final apiClient = container.read(apiClientProvider);
      // Keeps dio's real adapter, Sentry integrations like sentry_dio set
      // their headers there.
      final realAdapter = apiClient.httpClientAdapter;
      await signIn(container);
      apiClient.httpClientAdapter = realAdapter;

      await apiClient.get<void>('/v1/example');

      expect(receivedHeaderNames, contains('traceparent'));
      expect(
        receivedHeaderNames,
        isNot(anyOf(contains('sentry-trace'), contains('baggage'))),
      );
    });

    for (final (targetName, target) in [
      (
        'auth on the same host',
        AppEndpoints.of(
          Flavor.local,
          defaultTargetPlatform,
        ).auth.resolve('/api/auth/get-session'),
      ),
      ('a third party', Uri.https('example.com', '/data')),
    ]) {
      test(
        'request to $targetName carries neither traceparent nor access token',
        () async {
          final adapter = RecordingHttpClientAdapter();
          final apiClient = await createApiClient(
            adapter,
            flavor: Flavor.local,
          );

          await apiClient.getUri<void>(target);

          final headers = adapter.requests.single.headers;
          expect(headers.containsKey('traceparent'), isFalse);
          expect(headers.containsKey('Authorization'), isFalse);
        },
      );
    }

    test('request to the API while signed in carries the access token as bearer token', () async {
      final adapter = RecordingHttpClientAdapter();
      final apiClient = await createApiClient(adapter);

      await apiClient.get<void>('/v1/example');

      expect(
        adapter.requests.single.headers['Authorization'],
        'Bearer $issuedJsonWebToken',
      );
    });

    test('request to the API without a session is canceled without sending it or reporting a failure', () async {
      container = createContainer(await createPreferences());
      final adapter = RecordingHttpClientAdapter();
      final apiClient = container.read(apiClientProvider)
        ..httpClientAdapter = adapter;

      await expectLater(
        apiClient.get<void>('/v1/example'),
        throwsA(
          isA<DioException>().having(
            (error) => error.type,
            'type',
            DioExceptionType.cancel,
          ),
        ),
      );

      expect(adapter.requests, isEmpty);
      expect(container.read(reportedRequestFailureProvider), isNull);
    });

    test('request to the API logs method, path, status, duration and trace id without query, headers or body', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: (_) async => ResponseBody.fromString('{"weight": 81}', 200),
      );
      final apiClient = await createApiClient(adapter);

      await postWithQueryHeaderAndBody(apiClient);

      final log = container.read(talkerProvider).history.single;
      expect(log.logLevel, LogLevel.info);
      expect(
        log.message,
        allOf(
          matches(r'POST /v1/example 200 in \d+ ms'),
          contains(sentTraceId(adapter.requests.single)),
        ),
      );
      expect(log.message, isNot(containsQueryHeaderOrBody));
    });

    test('failed request to the API logs an error with status, duration and trace id without query, headers, body or the exception', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: (_) async => ResponseBody.fromString('{"weight": 81}', 500),
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        postWithQueryHeaderAndBody(apiClient),
        throwsA(isA<DioException>()),
      );

      final log = container.read(talkerProvider).history.single;
      expect(log.logLevel, LogLevel.error);
      expect(
        log.message,
        allOf(
          matches(r'POST /v1/example 500 in \d+ ms'),
          contains(sentTraceId(adapter.requests.single)),
        ),
      );
      expect(log.message, isNot(containsQueryHeaderOrBody));
      expect(log.exception, isNull);
    });

    test('failed request to the API throws an error whose text has the trace id but not the query', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: (request) async => throw DioException.connectionError(
          requestOptions: request,
          reason: 'Connection closed before full header was received',
          error: HttpException(
            'Connection closed before full header was received',
            uri: request.uri,
          ),
        ),
      );
      final apiClient = await createApiClient(adapter);

      DioException? error;
      try {
        await apiClient.get<void>(
          '/v1/example',
          queryParameters: {'search': 'apple'},
        );
      } on DioException catch (exception) {
        error = exception;
      }

      expect(
        error.toString(),
        allOf(
          contains(sentTraceId(adapter.requests.single)),
          isNot(contains('apple')),
        ),
      );
    });

    test('failed request to the API reports its trace id', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: respondWithServerError,
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        apiClient.get<void>('/v1/example'),
        throwsA(isA<DioException>()),
      );

      expect(
        container.read(reportedRequestFailureProvider)?.supportCode,
        sentTraceId(adapter.requests.single),
      );
    });

    test('request to the API failing with 503 reports serverUnavailable with its trace id as support code', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: (_) async => ResponseBody.fromString('', 503),
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        apiClient.get<void>('/v1/example'),
        throwsA(isA<DioException>()),
      );

      final report = container.read(reportedRequestFailureProvider);
      expect(report?.failure, RequestFailure.serverUnavailable);
      expect(report?.supportCode, sentTraceId(adapter.requests.single));
    });

    test('request to the API failing without a connection reports noConnection without a support code', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: (request) async => throw DioException.connectionError(
          requestOptions: request,
          reason: 'Connection refused',
        ),
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        apiClient.get<void>('/v1/example'),
        throwsA(isA<DioException>()),
      );

      final report = container.read(reportedRequestFailureProvider);
      expect(report?.failure, RequestFailure.noConnection);
      expect(report?.supportCode, isNull);
    });

    test('failed request to the API sends nothing to Sentry', () async {
      final envelopes = await startRecordingSentry();
      final adapter = RecordingHttpClientAdapter(
        respond: respondWithServerError,
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        apiClient.get<void>('/v1/example'),
        throwsA(isA<DioException>()),
      );

      expect(envelopes, isEmpty);
    });

    test('cancelled request to the API reports no failure', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: (request) async {
          request.cancelToken!.cancel();
          return ResponseBody.fromString('', 204);
        },
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        apiClient.get<void>('/v1/example', cancelToken: CancelToken()),
        throwsA(
          isA<DioException>().having(
            (error) => error.type,
            'type',
            DioExceptionType.cancel,
          ),
        ),
      );

      expect(container.read(reportedRequestFailureProvider), isNull);
    });

    test('failed request to another origin reports no failure', () async {
      final adapter = RecordingHttpClientAdapter(
        respond: respondWithServerError,
      );
      final apiClient = await createApiClient(adapter);

      await expectLater(
        apiClient.getUri<void>(Uri.https('example.com', '/data')),
        throwsA(isA<DioException>()),
      );

      expect(container.read(reportedRequestFailureProvider), isNull);
    });
  });
}

class _ProxyHttpOverrides extends HttpOverrides {
  _ProxyHttpOverrides(this.port);

  final int port;

  @override
  String findProxyFromEnvironment(Uri url, Map<String, String>? environment) =>
      'PROXY 127.0.0.1:$port';
}
