import 'package:dio/dio.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'request_failure.dart';

part 'reported_request_failure_provider.g.dart';

class RequestFailureReport {
  const RequestFailureReport({required this.failure, this.supportCode});

  final RequestFailure failure;
  final String? supportCode;
}

@Riverpod(keepAlive: true)
class ReportedRequestFailure extends _$ReportedRequestFailure {
  @override
  RequestFailureReport? build() => null;

  // Without a response the server never saw the trace, support can't find it.
  void report(DioException error, {String? traceId}) =>
      state = RequestFailureReport(
        failure: RequestFailure.of(error),
        supportCode: error.response == null ? null : traceId,
      );
}
