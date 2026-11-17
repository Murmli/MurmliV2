import 'package:dio/dio.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../flavor/app_endpoints_provider.dart';

part 'api_client_provider.g.dart';

@Riverpod(keepAlive: true)
Dio apiClient(Ref ref) {
  final dio = Dio(
    BaseOptions(baseUrl: ref.watch(appEndpointsProvider).api.toString()),
  );
  ref.onDispose(dio.close);
  return dio;
}
