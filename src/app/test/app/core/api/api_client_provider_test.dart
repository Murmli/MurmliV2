import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/flavor/app_endpoints.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/flavor/flavor_provider.dart';

void main() {
  group('apiClient', () {
    for (final flavor in [Flavor.local, Flavor.development]) {
      test('request in ${flavor.name} goes to the API of the flavor', () async {
        final container = ProviderContainer.test(
          overrides: [flavorProvider.overrideWithValue(flavor)],
        );
        final adapter = _RecordingHttpClientAdapter();
        final apiClient = container.read(apiClientProvider)
          ..httpClientAdapter = adapter;

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
  });
}

class _RecordingHttpClientAdapter implements HttpClientAdapter {
  final requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    return ResponseBody.fromString('', 204);
  }

  @override
  void close({bool force = false}) {}
}
