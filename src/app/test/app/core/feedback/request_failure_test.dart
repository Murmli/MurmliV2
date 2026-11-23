import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/feedback/request_failure.dart';

void main() {
  final requestOptions = RequestOptions(path: '/v1/example');

  DioException failureWithoutResponse(DioExceptionType type) =>
      DioException(requestOptions: requestOptions, type: type);

  DioException failureWithStatus(int statusCode) => DioException.badResponse(
    statusCode: statusCode,
    requestOptions: requestOptions,
    response: Response<void>(
      requestOptions: requestOptions,
      statusCode: statusCode,
    ),
  );

  group('RequestFailure.of', () {
    for (final (type, expectedFailure) in [
      (DioExceptionType.connectionError, RequestFailure.noConnection),
      (DioExceptionType.connectionTimeout, RequestFailure.noConnection),
      (DioExceptionType.sendTimeout, RequestFailure.noConnection),
      (DioExceptionType.receiveTimeout, RequestFailure.noConnection),
      (DioExceptionType.unknown, RequestFailure.unexpected),
      (DioExceptionType.badCertificate, RequestFailure.unexpected),
    ]) {
      test(
        'failure of type ${type.name} without response is ${expectedFailure.name}',
        () {
          expect(
            RequestFailure.of(failureWithoutResponse(type)),
            expectedFailure,
          );
        },
      );
    }

    for (final (statusCodes, expectedFailure) in [
      ([408], RequestFailure.noConnection),
      ([502, 503, 504], RequestFailure.serverUnavailable),
      ([429], RequestFailure.tooManyRequests),
      ([401], RequestFailure.unauthorized),
      ([403], RequestFailure.forbidden),
      ([404, 410], RequestFailure.notFound),
      ([400, 409, 413, 422], RequestFailure.invalidRequest),
      ([500, 501], RequestFailure.serverError),
    ]) {
      for (final statusCode in statusCodes) {
        test('response with status $statusCode is ${expectedFailure.name}', () {
          expect(
            RequestFailure.of(failureWithStatus(statusCode)),
            expectedFailure,
          );
        });
      }
    }
  });

  group('RequestFailure.isTransient', () {
    test(
      'is true for noConnection, serverUnavailable and tooManyRequests only',
      () {
        expect(
          RequestFailure.values.where((failure) => failure.isTransient),
          unorderedEquals([
            RequestFailure.noConnection,
            RequestFailure.serverUnavailable,
            RequestFailure.tooManyRequests,
          ]),
        );
      },
    );
  });
}
