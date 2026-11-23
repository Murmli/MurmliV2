import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../router/app_router_provider.dart';
import 'popup.dart';
import 'reported_request_failure_provider.dart';
import 'request_failure.dart';
import 'toast.dart';

class RequestFailureListener extends HookConsumerWidget {
  const RequestFailureListener({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isPopupOpen = useRef(false);
    final navigatorKey = ref.watch(appRouterProvider).navigatorKey;
    ref.listen(reportedRequestFailureProvider, (_, report) {
      final navigatorContext = navigatorKey.currentContext;
      if (report == null || navigatorContext == null || !_isAppVisible()) {
        return;
      }
      final failure = report.failure;
      if (failure.isTransient) {
        final (:message, :icon) = _toastOf(failure, navigatorContext);
        showToast(
          navigatorContext,
          message,
          tone: ToastTone.warning,
          icon: icon,
        );
        return;
      }
      if (isPopupOpen.value) {
        return;
      }
      final (:title, :message) = _popupTextsOf(failure, navigatorContext);
      isPopupOpen.value = true;
      unawaited(
        showMessagePopup(
          navigatorContext,
          title: title,
          message: message,
          supportCode: report.supportCode,
        ).whenComplete(() => isPopupOpen.value = false),
      );
    });
    return child;
  }
}

bool _isAppVisible() => switch (WidgetsBinding.instance.lifecycleState) {
  AppLifecycleState.hidden ||
  AppLifecycleState.paused ||
  AppLifecycleState.detached => false,
  AppLifecycleState.resumed || AppLifecycleState.inactive || null => true,
};

({String message, IconData icon}) _toastOf(
  RequestFailure failure,
  BuildContext context,
) {
  final translations = context.t.requestFailure;
  return switch (failure) {
    RequestFailure.noConnection => (
      message: translations.noConnection,
      icon: Icons.wifi_off_rounded,
    ),
    RequestFailure.serverUnavailable => (
      message: translations.serverUnavailable,
      icon: Icons.cloud_off_rounded,
    ),
    RequestFailure.tooManyRequests => (
      message: translations.tooManyRequests,
      icon: Icons.hourglass_empty_rounded,
    ),
    RequestFailure.unauthorized ||
    RequestFailure.forbidden ||
    RequestFailure.notFound ||
    RequestFailure.invalidRequest ||
    RequestFailure.serverError ||
    RequestFailure.unexpected => throw ArgumentError.value(
      failure,
      'failure',
      'is not transient',
    ),
  };
}

({String title, String message}) _popupTextsOf(
  RequestFailure failure,
  BuildContext context,
) {
  final translations = context.t.requestFailure;
  return switch (failure) {
    RequestFailure.unauthorized => (
      title: translations.unauthorized.title,
      message: translations.unauthorized.message,
    ),
    RequestFailure.forbidden => (
      title: translations.forbidden.title,
      message: translations.forbidden.message,
    ),
    RequestFailure.notFound => (
      title: translations.notFound.title,
      message: translations.notFound.message,
    ),
    RequestFailure.invalidRequest => (
      title: translations.invalidRequest.title,
      message: translations.invalidRequest.message,
    ),
    RequestFailure.serverError => (
      title: translations.serverError.title,
      message: translations.serverError.message,
    ),
    RequestFailure.unexpected => (
      title: translations.unexpected.title,
      message: translations.unexpected.message,
    ),
    RequestFailure.noConnection ||
    RequestFailure.serverUnavailable ||
    RequestFailure.tooManyRequests => throw ArgumentError.value(
      failure,
      'failure',
      'is transient',
    ),
  };
}
