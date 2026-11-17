import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

class RecordingHttpClientAdapter implements HttpClientAdapter {
  RecordingHttpClientAdapter({this.respond = _respondWithNoContent});

  final Future<ResponseBody> Function(RequestOptions request) respond;
  final requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) {
    requests.add(options);
    return respond(options);
  }

  @override
  void close({bool force = false}) {}
}

Future<ResponseBody> _respondWithNoContent(RequestOptions request) async =>
    ResponseBody.fromString('', 204);

String sentTraceId(RequestOptions request) =>
    (request.headers['traceparent'] as String).split('-')[1];
