import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'failed_api_request_trace_id_provider.g.dart';

@Riverpod(keepAlive: true)
class FailedApiRequestTraceId extends _$FailedApiRequestTraceId {
  @override
  String? build() => null;

  void report(String traceId) => state = traceId;
}
