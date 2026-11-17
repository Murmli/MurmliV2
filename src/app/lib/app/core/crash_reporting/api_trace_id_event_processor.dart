import 'dart:async';

import 'package:dio/dio.dart';
import 'package:hooks_riverpod/misc.dart' show ProviderException;
import 'package:sentry_flutter/sentry_flutter.dart';

import '../api/api_trace_interceptor.dart';

class ApiTraceIdEventProcessor implements EventProcessor {
  @override
  FutureOr<SentryEvent?> apply(SentryEvent event, Hint hint) {
    Object? throwable = event.throwable;
    while (throwable is ProviderException) {
      throwable = throwable.exception;
    }
    final traceId = throwable is DioException
        ? throwable.requestOptions.apiTraceId
        : null;
    if (traceId != null) {
      event.tags = {...?event.tags, 'api_trace_id': traceId};
    }
    return event;
  }
}
