import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router_provider.dart';
import '../../../core/widgets/message_dialog.dart';
import '../providers/apple_disconnect_notice_provider.dart';

class AppleDisconnectNoticeListener extends ConsumerWidget {
  const AppleDisconnectNoticeListener({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    ref.listen(pendingAppleDisconnectNoticeProvider, (_, isPending) {
      if (!isPending) {
        return;
      }
      // The redirect to onboarding replaces the pages in the next frame and
      // would remove a dialog shown before it.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (context.mounted) {
          _showNotice(ref);
        }
      });
    });
    return child;
  }

  void _showNotice(WidgetRef ref) {
    final navigatorContext = ref
        .read(appRouterProvider)
        .navigatorKey
        .currentContext;
    if (navigatorContext == null) {
      return;
    }
    ref.read(pendingAppleDisconnectNoticeProvider.notifier).dismiss();
    final translations =
        navigatorContext.t.settings.account.appleDisconnectDialog;
    unawaited(
      showMessageDialog(
        navigatorContext,
        title: translations.title,
        message: translations.message,
        confirmLabel: translations.confirm,
      ),
    );
  }
}
