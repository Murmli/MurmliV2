import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:dio/io.dart';

enum SyncFault { none, requestLost, responseLost }

typedef RecordedSyncResponse = ({int statusCode, Map<String, Object?>? body});

class FaultInjectingHttpClientAdapter implements HttpClientAdapter {
  FaultInjectingHttpClientAdapter({required this.onSyncResponse});

  final void Function(RecordedSyncResponse response) onSyncResponse;
  final _adapter = IOHttpClientAdapter();
  SyncFault fault = SyncFault.none;
  bool isOffline = false;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    if (options.method != 'POST' || options.uri.path != '/v1/sync') {
      return _adapter.fetch(options, requestStream, cancelFuture);
    }
    if (isOffline || fault == SyncFault.requestLost) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'The simulation lost the request.',
      );
    }
    final response = await _adapter.fetch(options, requestStream, cancelFuture);
    final bodyBuilder = BytesBuilder(copy: false);
    await response.stream.forEach(bodyBuilder.add);
    final body = bodyBuilder.takeBytes();
    onSyncResponse((
      statusCode: response.statusCode,
      body: response.statusCode == 200
          ? jsonDecode(utf8.decode(body)) as Map<String, Object?>
          : null,
    ));
    if (fault == SyncFault.responseLost) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'The simulation lost the response.',
      );
    }
    return ResponseBody.fromBytes(
      body,
      response.statusCode,
      statusMessage: response.statusMessage,
      headers: response.headers,
    );
  }

  @override
  void close({bool force = false}) => _adapter.close(force: force);
}
