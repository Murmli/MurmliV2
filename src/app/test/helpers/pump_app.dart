import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/app.dart';
import 'package:app/i18n/strings.g.dart';

Future<void> pumpApp(WidgetTester tester) async {
  tester.platformDispatcher.localesTestValue = const [Locale('de', 'DE')];
  addTearDown(tester.platformDispatcher.clearLocalesTestValue);
  await LocaleSettings.useDeviceLocale();

  await tester.pumpWidget(
    ProviderScope(child: TranslationProvider(child: const MurmliApp())),
  );
  await tester.pumpAndSettle();
}
