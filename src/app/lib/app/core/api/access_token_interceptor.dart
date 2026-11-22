import 'dart:async';

import 'package:dio/dio.dart';

class AccessTokenInterceptor extends Interceptor {
  AccessTokenInterceptor({
    required Uri api,
    required this._accessToken,
    required this._retry,
  }) : _apiOrigin = api.origin;

  final String _apiOrigin;
  final Future<String?> Function({String? rejected}) _accessToken;
  final Future<Response<dynamic>> Function(RequestOptions options) _retry;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    if (options.uri.origin == _apiOrigin) {
      unawaited(_authorize(options, handler));
    } else {
      handler.next(options);
    }
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final options = err.requestOptions;
    final sentAccessToken = options.extra[_sentAccessTokenKey] as String?;
    if (err.response?.statusCode == 401 &&
        options.uri.origin == _apiOrigin &&
        sentAccessToken != null &&
        options.extra[_retriedKey] != true) {
      unawaited(_retryWithRenewedToken(err, sentAccessToken, handler));
    } else {
      handler.next(err);
    }
  }

  Future<void> _authorize(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final String? accessToken;
    try {
      accessToken = await _accessToken();
    } on DioException catch (error) {
      // Carries over what failed at auth, but not its request with URL and
      // session token.
      handler.reject(
        DioException(
          requestOptions: options,
          type: error.type,
          error: error.error,
          response: switch (error.response) {
            final response? => Response<void>(
              requestOptions: options,
              statusCode: response.statusCode,
              statusMessage: response.statusMessage,
            ),
            null => null,
          },
        ),
        true,
      );
      return;
    }
    if (accessToken == null) {
      handler.reject(_withoutSession(options));
      return;
    }
    options
      ..headers['Authorization'] = 'Bearer $accessToken'
      ..extra[_sentAccessTokenKey] = accessToken;
    handler.next(options);
  }

  Future<void> _retryWithRenewedToken(
    DioException error,
    String sentAccessToken,
    ErrorInterceptorHandler handler,
  ) async {
    final options = error.requestOptions;
    final String? renewedAccessToken;
    try {
      renewedAccessToken = await _accessToken(rejected: sentAccessToken);
    } on DioException {
      handler.next(error);
      return;
    }
    if (renewedAccessToken == null) {
      handler.next(_withoutSession(options));
      return;
    }
    try {
      handler.resolve(
        await _retry(
          options.copyWith(extra: {...options.extra, _retriedKey: true}),
        ),
      );
    } on DioException catch (retryError) {
      // The retry passed all interceptors itself and was reported there.
      handler.reject(retryError);
    }
  }

  DioException _withoutSession(RequestOptions options) => DioException(
    requestOptions: options,
    type: DioExceptionType.cancel,
    message: 'There is no session.',
  );
}

const _sentAccessTokenKey = 'sentAccessToken';
const _retriedKey = 'retriedWithRenewedAccessToken';
