import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../i18n/strings.g.dart';
import 'core/authentication/signed_in_account_provider.dart';
import 'core/feedback/request_failure_listener.dart';
import 'core/router/app_router.dart';
import 'core/router/app_router_provider.dart';
import 'core/theme/app_theme.dart';
import 'core/widgets/flavor_banner.dart';
import 'features/settings/providers/theme_mode_provider.dart';
import 'features/settings/widgets/apple_disconnect_notice_listener.dart';

class MurmliApp extends HookConsumerWidget {
  const MurmliApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);
    final routerConfig = useMemoized(router.config, [router]);
    ref.listen(signedInAccountProvider, (previous, account) {
      if (previous != null && account == null) {
        unawaited(router.replaceAll([const OnboardingRoute()]));
      }
    });
    return MaterialApp.router(
      routerConfig: routerConfig,
      onGenerateTitle: (context) => context.t.app.title,
      theme: AppTheme.light,
      darkTheme: AppTheme.dark,
      themeMode: ref.watch(appThemeModeProvider),
      locale: TranslationProvider.of(context).flutterLocale,
      supportedLocales: AppLocaleUtils.supportedLocales,
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      builder: (context, child) => RequestFailureListener(
        child: AppleDisconnectNoticeListener(
          child: FlavorBanner(child: child!),
        ),
      ),
    );
  }
}
