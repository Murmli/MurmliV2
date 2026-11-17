import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../api/failed_api_request_trace_id_provider.dart';
import '../router/app_router_provider.dart';
import '../theme/app_spacing.dart';

class ApiErrorDialogListener extends HookConsumerWidget {
  const ApiErrorDialogListener({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isDialogOpen = useRef(false);
    final navigatorKey = ref.watch(appRouterProvider).navigatorKey;
    ref.listen(failedApiRequestTraceIdProvider, (_, traceId) {
      final navigatorContext = navigatorKey.currentContext;
      if (traceId == null ||
          isDialogOpen.value ||
          navigatorContext == null ||
          !_isAppVisible()) {
        return;
      }
      isDialogOpen.value = true;
      unawaited(
        showDialog<void>(
          context: navigatorContext,
          builder: (_) => _ApiErrorDialog(traceId: traceId),
        ).whenComplete(() => isDialogOpen.value = false),
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

class _ApiErrorDialog extends StatelessWidget {
  const _ApiErrorDialog({required this.traceId});

  final String traceId;

  @override
  Widget build(BuildContext context) {
    final translations = context.t.apiError;
    return AlertDialog(
      title: Text(translations.title),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(translations.message),
          const SizedBox(height: AppSpacing.space12),
          SelectableText(traceId, style: TextTheme.of(context).titleSmall),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(translations.close),
        ),
      ],
    );
  }
}
