import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/app.dart';
import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/features/settings/providers/app_language_provider.dart';
import 'package:app/i18n/strings.g.dart';

import 'preferences.dart';
import 'provider_container.dart';

Future<ProviderContainer> pumpApp(
  WidgetTester tester, {
  Map<String, Object> storedPreferences = const {},
  Flavor? flavor,
  Account? signedInAccount,
}) async {
  tester.platformDispatcher.localesTestValue = const [Locale('de', 'DE')];
  addTearDown(tester.platformDispatcher.clearLocalesTestValue);

  final container = createContainer(
    await createPreferences(storedPreferences),
    flavor: flavor,
    signedInAccount: signedInAccount,
  );
  await container.read(selectedAppLanguageProvider).apply();

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: TranslationProvider(child: const MurmliApp()),
    ),
  );
  await tester.pumpAndSettle();
  return container;
}
