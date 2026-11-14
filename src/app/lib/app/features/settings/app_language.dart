import '../../../i18n/strings.g.dart';

enum AppLanguage {
  system,
  german,
  english;

  Future<void> apply() async {
    switch (this) {
      case AppLanguage.system:
        await LocaleSettings.useDeviceLocale();
      case AppLanguage.german:
        await LocaleSettings.setLocale(AppLocale.de);
      case AppLanguage.english:
        await LocaleSettings.setLocale(AppLocale.en);
    }
  }
}
