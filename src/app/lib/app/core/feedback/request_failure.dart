import 'package:dio/dio.dart';

enum RequestFailure {
  noConnection,
  serverUnavailable,
  tooManyRequests,
  unauthorized,
  forbidden,
  notFound,
  invalidRequest,
  serverError,
  unexpected;

  factory RequestFailure.of(DioException error) =>
      switch (error.response?.statusCode) {
        408 => noConnection,
        429 => tooManyRequests,
        502 || 503 || 504 => serverUnavailable,
        401 => unauthorized,
        403 => forbidden,
        404 || 410 => notFound,
        final int status when status >= 400 && status < 500 => invalidRequest,
        final int status when status >= 500 && status < 600 => serverError,
        null => switch (error.type) {
          DioExceptionType.connectionError ||
          DioExceptionType.connectionTimeout ||
          DioExceptionType.sendTimeout ||
          DioExceptionType.receiveTimeout => noConnection,
          _ => unexpected,
        },
        _ => unexpected,
      };

  bool get isTransient => switch (this) {
    noConnection || serverUnavailable || tooManyRequests => true,
    unauthorized ||
    forbidden ||
    notFound ||
    invalidRequest ||
    serverError ||
    unexpected => false,
  };
}
