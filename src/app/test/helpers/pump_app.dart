import 'package:clock/clock.dart';
import 'package:dio/dio.dart';
import 'package:drift/drift.dart' show Value;
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/app.dart';
import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/database/app_database_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/settings/providers/app_language_provider.dart';
import 'package:app/i18n/strings.g.dart';

import 'authentication.dart';
import 'preferences.dart';
import 'provider_container.dart';

Future<ProviderContainer> pumpApp(
  WidgetTester tester, {
  Map<String, Object> storedPreferences = const {},
  Flavor? flavor,
  bool? debugBuild,
  Account? signedInAccount,
  Clock? clock,
  Future<ResponseBody> Function(RequestOptions request) respondFromAuth =
      respondAsAuth,
}) async {
  tester.platformDispatcher.localesTestValue = const [Locale('de', 'DE')];
  addTearDown(tester.platformDispatcher.clearLocalesTestValue);

  final container = createContainer(
    await createPreferences({
      PreferenceKeys.installed: true,
      ...storedPreferences,
    }),
    flavor: flavor,
    debugBuild: debugBuild,
    clock: clock,
    secureStorage: {
      if (signedInAccount != null) ...storedSession(signedInAccount.id),
    },
  );
  // production has no auth address, signed-out tests make no auth requests.
  if (signedInAccount != null) {
    recordAuthenticationRequests(container, respondFromAuth: respondFromAuth);
    await _completeFirstSync(tester, container, signedInAccount);
  }
  await container.read(signedInAccountProvider.notifier).restore();
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

/// Stores the state after the first completed sync of [account], so the sync
/// core accepts writes without a round against a server.
Future<void> _completeFirstSync(
  WidgetTester tester,
  ProviderContainer container,
  Account account,
) => tester.runAsync(() {
  final database = container.read(appDatabaseProvider);
  return database
      .into(database.syncStates)
      .insert(
        SyncStatesCompanion.insert(
          id: const Value(1),
          accountId: Value(account.id),
          firstSyncCompleted: const Value(true),
        ),
      );
});
