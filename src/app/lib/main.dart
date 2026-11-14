import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'app/app.dart';
import 'i18n/strings.g.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  LicenseRegistry.addLicense(_fontLicenses);
  await LocaleSettings.useDeviceLocale();

  runApp(ProviderScope(child: TranslationProvider(child: const MurmliApp())));
}

Stream<LicenseEntry> _fontLicenses() async* {
  const fontLicenses = [
    ('Bricolage Grotesque', 'assets/fonts/BricolageGrotesque-OFL.txt'),
    ('Plus Jakarta Sans', 'assets/fonts/PlusJakartaSans-OFL.txt'),
  ];
  for (final (fontName, licenseAsset) in fontLicenses) {
    final licenseText = await rootBundle.loadString(licenseAsset);
    yield LicenseEntryWithLineBreaks([fontName], licenseText);
  }
}
