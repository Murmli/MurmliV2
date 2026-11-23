import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:dio/dio.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/feedback/popup.dart';
import '../../../core/feedback/reported_request_failure_provider.dart';
import '../../../core/feedback/request_failure.dart';
import '../../../core/feedback/toast.dart';
import '../../../core/theme/app_spacing.dart';

@RoutePage()
class CaloriesPage extends StatelessWidget {
  const CaloriesPage({super.key});

  @override
  Widget build(BuildContext context) => const _FeedbackDemo();
}

class _FeedbackDemo extends ConsumerWidget {
  const _FeedbackDemo();

  static const _navigationBarHeight = 68.0;
  static const _traceId = '4bf92f3577b34da6a3ce929d0e0e4736';

  @override
  Widget build(BuildContext context, WidgetRef ref) => ListView(
    padding: EdgeInsetsDirectional.only(
      start: AppSpacing.screenMargin,
      end: AppSpacing.screenMargin,
      top: MediaQuery.paddingOf(context).top + AppSpacing.space32,
      bottom:
          MediaQuery.paddingOf(context).bottom +
          _navigationBarHeight +
          AppSpacing.space32,
    ),
    children: [
      for (final failure in RequestFailure.values) ...[
        OutlinedButton(
          onPressed: () => ref
              .read(reportedRequestFailureProvider.notifier)
              .report(_errorOf(failure), traceId: _traceId),
          child: Text(failure.name),
        ),
        const SizedBox(height: AppSpacing.space12),
      ],
      OutlinedButton(
        onPressed: () => unawaited(_confirm(context)),
        child: const Text('showConfirmationPopup'),
      ),
      const SizedBox(height: AppSpacing.space12),
      OutlinedButton(
        onPressed: () =>
            showToast(context, 'Something went wrong.', tone: ToastTone.error),
        child: const Text('toast error'),
      ),
      const SizedBox(height: AppSpacing.space12),
      OutlinedButton(
        onPressed: () => showToast(context, 'Saved your changes.'),
        child: const Text('toast info'),
      ),
    ],
  );

  DioException _errorOf(RequestFailure failure) {
    final requestOptions = RequestOptions();
    DioException responseError(int statusCode) => DioException(
      requestOptions: requestOptions,
      type: DioExceptionType.badResponse,
      response: Response(
        requestOptions: requestOptions,
        statusCode: statusCode,
      ),
    );
    return switch (failure) {
      RequestFailure.noConnection => DioException(
        requestOptions: requestOptions,
        type: DioExceptionType.connectionError,
      ),
      RequestFailure.unexpected => DioException(
        requestOptions: requestOptions,
        type: DioExceptionType.unknown,
      ),
      RequestFailure.serverUnavailable => responseError(503),
      RequestFailure.tooManyRequests => responseError(429),
      RequestFailure.unauthorized => responseError(401),
      RequestFailure.forbidden => responseError(403),
      RequestFailure.notFound => responseError(404),
      RequestFailure.invalidRequest => responseError(400),
      RequestFailure.serverError => responseError(500),
    };
  }

  Future<void> _confirm(BuildContext context) async {
    final translations = context.t.settings.account.deleteDialog;
    final isConfirmed = await showConfirmationPopup(
      context,
      title: translations.title,
      message: translations.message,
      cancelLabel: translations.cancel,
      confirmLabel: translations.confirm,
    );
    if (context.mounted) {
      showToast(context, '$isConfirmed');
    }
  }
}
