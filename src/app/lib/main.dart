import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:talker_flutter/talker_flutter.dart';

import 'app/app.dart';
import 'app/core/authentication/signed_in_account_provider.dart';
import 'app/core/crash_reporting/crash_reporting_provider.dart';
import 'app/core/database/signed_out_data_wipe_provider.dart';
import 'app/core/logging/talker_provider.dart';
import 'app/core/storage/preference_keys.dart';
import 'app/core/storage/shared_preferences_provider.dart';
import 'app/features/settings/providers/app_language_provider.dart';
import 'app/features/shopping_list/providers/first_shopping_list_provider.dart';
import 'i18n/strings.g.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final talker = TalkerFlutter.init();
  FlutterError.onError = (details) => talker.handle(
    details.exception,
    details.stack,
    details.context?.toString(),
  );
  PlatformDispatcher.instance.onError = (error, stack) {
    talker.handle(error, stack);
    return true;
  };
  LicenseRegistry.addLicense(_fontLicenses);

  final sharedPreferences = await SharedPreferencesWithCache.create(
    cacheOptions: const SharedPreferencesWithCacheOptions(
      allowList: PreferenceKeys.all,
    ),
  );
  final container = ProviderContainer(
    overrides: [
      sharedPreferencesProvider.overrideWithValue(sharedPreferences),
      talkerProvider.overrideWithValue(talker),
    ],
  );
  await container.read(crashReportingProvider.notifier).start();
  await container.read(signedInAccountProvider.notifier).restore();
  container
    ..read(signedOutDataWipeProvider)
    ..read(firstShoppingListProvider);
  await container.read(selectedAppLanguageProvider).apply();

  runApp(
    UncontrolledProviderScope(
      container: container,
      child: TranslationProvider(child: const MurmliApp()),
    ),
  );
}

Stream<LicenseEntry> _fontLicenses() async* {
  const fontLicenses = [
    ('Bricolage Grotesque', 'assets/fonts/BricolageGrotesque-OFL.txt'),
    ('Google Sans', 'assets/fonts/GoogleSans-OFL.txt'),
    ('Plus Jakarta Sans', 'assets/fonts/PlusJakartaSans-OFL.txt'),
  ];
  for (final (fontName, licenseAsset) in fontLicenses) {
    final licenseText = await rootBundle.loadString(licenseAsset);
    yield LicenseEntryWithLineBreaks([fontName], licenseText);
  }
}
