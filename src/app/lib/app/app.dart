import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../i18n/strings.g.dart';
import 'core/router/app_router_provider.dart';
import 'core/theme/app_theme.dart';
import 'features/settings/theme_mode_provider.dart';

class MurmliApp extends HookConsumerWidget {
  const MurmliApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);
    final routerConfig = useMemoized(router.config, [router]);
    return MaterialApp.router(
      routerConfig: routerConfig,
      onGenerateTitle: (context) => context.t.app.title,
      theme: AppTheme.light,
      darkTheme: AppTheme.dark,
      themeMode: ref.watch(appThemeModeProvider),
      locale: TranslationProvider.of(context).flutterLocale,
      supportedLocales: AppLocaleUtils.supportedLocales,
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
    );
  }
}
