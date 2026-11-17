import 'package:dio/dio.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../flavor/app_endpoints_provider.dart';
import '../logging/talker_provider.dart';
import 'api_trace_interceptor.dart';
import 'failed_api_request_trace_id_provider.dart';

part 'api_client_provider.g.dart';

@Riverpod(keepAlive: true)
Dio apiClient(Ref ref) {
  final api = ref.watch(appEndpointsProvider).api;
  final failedApiRequestTraceId = ref.watch(
    failedApiRequestTraceIdProvider.notifier,
  );
  final dio = Dio(BaseOptions(baseUrl: api.toString()))
    ..interceptors.addAll([
      ApiTraceInterceptor(api: api, talker: ref.watch(talkerProvider)),
      InterceptorsWrapper(
        onError: (error, handler) {
          final traceId = error.requestOptions.apiTraceId;
          if (traceId != null && error.type != DioExceptionType.cancel) {
            failedApiRequestTraceId.report(traceId);
          }
          handler.next(error);
        },
      ),
    ]);
  ref.onDispose(dio.close);
  return dio;
}
