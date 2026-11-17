import 'dart:math';

import 'package:dio/dio.dart';
import 'package:talker/talker.dart';

class ApiTraceInterceptor extends Interceptor {
  ApiTraceInterceptor({required Uri api, required this.talker})
    : _apiOrigin = api.origin;

  final String _apiOrigin;
  final Talker talker;
  final _random = Random.secure();

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    if (options.uri.origin == _apiOrigin) {
      final trace = _ApiTrace(_randomHexId(byteCount: 16));
      options
        ..headers['traceparent'] =
            '00-${trace.traceId}-${_randomHexId(byteCount: 8)}-01'
        ..extra[_apiTraceKey] = trace;
    }
    handler.next(options);
  }

  @override
  void onResponse(
    Response<dynamic> response,
    ResponseInterceptorHandler handler,
  ) {
    final summary = _summarize(
      response.requestOptions,
      '${response.statusCode}',
    );
    if (summary != null) {
      talker.info(summary);
    }
    handler.next(response);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final summary = _summarize(
      err.requestOptions,
      err.response?.statusCode?.toString() ?? err.type.name,
    );
    if (summary != null) {
      // main.dart logs uncaught errors via toString(), and dio's default text
      // contains the error, e.g. an HttpException with the full URL and query.
      err.stringBuilder = (_) => 'DioException: $summary';
      talker.log(
        summary,
        logLevel: err.type == DioExceptionType.cancel
            ? LogLevel.info
            : LogLevel.error,
      );
    }
    handler.next(err);
  }

  String? _summarize(RequestOptions options, String outcome) {
    final trace = options._apiTrace;
    if (trace == null) {
      return null;
    }
    return '${options.method} ${options.uri.path} $outcome '
        'in ${trace.stopwatch.elapsedMilliseconds} ms, trace id ${trace.traceId}';
  }

  String _randomHexId({required int byteCount}) {
    List<int> bytes;
    // W3C Trace Context treats an all-zero id as invalid.
    do {
      bytes = List.generate(byteCount, (_) => _random.nextInt(256));
    } while (bytes.every((byte) => byte == 0));
    return bytes.map((byte) => byte.toRadixString(16).padLeft(2, '0')).join();
  }
}

extension ApiTraceRequestOptions on RequestOptions {
  String? get apiTraceId => _apiTrace?.traceId;

  _ApiTrace? get _apiTrace => extra[_apiTraceKey] as _ApiTrace?;
}

const _apiTraceKey = 'apiTrace';

class _ApiTrace {
  _ApiTrace(this.traceId);

  final String traceId;
  final stopwatch = Stopwatch()..start();
}
