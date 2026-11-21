import 'dart:collection';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:dio/io.dart';

enum SyncFault {
  none,
  requestLost,
  responseLost,
  serverUnavailable,
  failedAfterProcessing,
  truncatedResponse,
}

typedef RecordedSyncResponse = ({int statusCode, Map<String, Object?>? body});

typedef SyncExchange = ({SyncFault fault, RecordedSyncResponse? response});

class FaultInjectingHttpClientAdapter implements HttpClientAdapter {
  FaultInjectingHttpClientAdapter({
    required this.onSyncRequest,
    required this.onSyncResponse,
  });

  final Future<void> Function() onSyncRequest;
  final void Function(RecordedSyncResponse response) onSyncResponse;
  final _adapter = IOHttpClientAdapter();

  /// Assigned to the sync requests in their order of arrival.
  final faults = Queue<SyncFault>();

  /// The fault and the server response of each sync request, by the cancel
  /// token of the round that sent it.
  final exchanges = <CancelToken?, SyncExchange>{};
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
    await onSyncRequest();
    final fault = faults.removeFirst();
    exchanges[options.cancelToken] = (fault: fault, response: null);
    if (isOffline || fault == SyncFault.requestLost) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'The simulation lost the request.',
      );
    }
    if (fault == SyncFault.serverUnavailable) {
      return ResponseBody.fromBytes(Uint8List(0), 503);
    }
    final response = await _adapter.fetch(options, requestStream, cancelFuture);
    final bodyBuilder = BytesBuilder(copy: false);
    await response.stream.forEach(bodyBuilder.add);
    final body = bodyBuilder.takeBytes();
    final recordedResponse = (
      statusCode: response.statusCode,
      body: response.statusCode == 200
          ? jsonDecode(utf8.decode(body)) as Map<String, Object?>
          : null,
    );
    exchanges[options.cancelToken] = (fault: fault, response: recordedResponse);
    onSyncResponse(recordedResponse);
    if (fault == SyncFault.responseLost) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'The simulation lost the response.',
      );
    }
    if (fault == SyncFault.failedAfterProcessing) {
      return ResponseBody.fromBytes(Uint8List(0), 500);
    }
    return ResponseBody.fromBytes(
      fault == SyncFault.truncatedResponse
          ? Uint8List.sublistView(body, 0, body.length ~/ 2)
          : body,
      response.statusCode,
      statusMessage: response.statusMessage,
      headers: response.headers,
    );
  }

  @override
  void close({bool force = false}) => _adapter.close(force: force);
}
