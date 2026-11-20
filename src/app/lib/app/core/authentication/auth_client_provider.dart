import 'package:dio/dio.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../flavor/app_endpoints_provider.dart';

part 'auth_client_provider.g.dart';

@Riverpod(keepAlive: true)
Dio authClient(Ref ref) {
  final dio = Dio(
    BaseOptions(baseUrl: ref.watch(appEndpointsProvider).auth.toString()),
  );
  ref.onDispose(dio.close);
  return dio;
}
