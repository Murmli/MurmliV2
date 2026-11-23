import 'package:dio/dio.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../authentication/signed_in_account_provider.dart';
import '../feedback/reported_request_failure_provider.dart';
import '../flavor/app_endpoints_provider.dart';
import '../logging/talker_provider.dart';
import 'access_token_interceptor.dart';
import 'api_trace_interceptor.dart';

part 'api_client_provider.g.dart';

@Riverpod(keepAlive: true)
Dio apiClient(Ref ref) {
  final api = ref.watch(appEndpointsProvider).api;
  final reportedRequestFailure = ref.watch(
    reportedRequestFailureProvider.notifier,
  );
  final dio = Dio(BaseOptions(baseUrl: api.toString()));
  dio.interceptors.addAll([
    ApiTraceInterceptor(api: api, talker: ref.watch(talkerProvider)),
    AccessTokenInterceptor(
      api: api,
      accessToken: ({rejected}) => ref
          .read(signedInAccountProvider.notifier)
          .accessToken(rejected: rejected),
      retry: dio.fetch,
    ),
    InterceptorsWrapper(
      onError: (error, handler) {
        final traceId = error.requestOptions.apiTraceId;
        if (traceId != null && error.type != DioExceptionType.cancel) {
          reportedRequestFailure.report(error, traceId: traceId);
        }
        handler.next(error);
      },
    ),
  ]);
  ref.onDispose(dio.close);
  return dio;
}
