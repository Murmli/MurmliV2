# App

Die Flutter-App liegt in `src/app` (Pfade unten relativ dazu), die Flutter-Version steht in `src/app/.fvmrc`, Befehle laufen mit `fvm`.

## Flavors

Die App hat die Flavors `local`, `development` und `production`. `pubspec.yaml` setzt unter `flutter:` `default-flavor: development`, jeder `flutter`-Befehl ohne `--flavor` (`run`, `build`, `test`) nutzt deshalb `development`. Alle Flavors teilen die App-ID `de.murmli.app` und lassen sich nicht nebeneinander installieren.

- `lib/app/core/flavor/flavor.dart`: Enum `Flavor` mit dem Text des Banners (`local`, `dev`, für `production` keiner).
- `lib/app/core/flavor/flavor_provider.dart`: `flavorProvider` liest `appFlavor`, eine Compile-Zeit-Konstante aus `package:flutter/services.dart`, gesetzt per `--flavor` oder `default-flavor`.
- `lib/app/core/flavor/app_endpoints.dart`: `AppEndpoints` mit den Adressen von API und `auth` pro Flavor. `app_endpoints_provider.dart` stellt sie als `appEndpointsProvider` bereit. Noch nutzt ihn niemand, der gemeinsame API-Zugriff kommt mit [#52](https://github.com/Murmli/MurmliV2/issues/52). Dieser Satz entfällt mit [#52](https://github.com/Murmli/MurmliV2/issues/52).

| Flavor | API | Anmeldung (`auth`) |
| --- | --- | --- |
| `local` | `http://localhost:5020` (iOS-Simulator), `http://10.0.2.2:5020` (Android-Emulator) | `http://localhost:5021` (iOS-Simulator), `http://10.0.2.2:5021` (Android-Emulator) |
| `development` | `https://api.kinddune-9cab4a57.francecentral.azurecontainerapps.io` | `https://auth.kinddune-9cab4a57.francecentral.azurecontainerapps.io` |
| `production` | noch keine | noch keine |

- `local` braucht ein laufendes `aspire run` (Voraussetzungen in [AGENTS.md](../AGENTS.md#aspire-und-azure)). Der Port der API kommt aus ihrem Launch-Profil, der von `auth` steht fest in `AddAuth()`. `10.0.2.2` ist der Alias des Android-Emulators für das Loopback des Hosts.
- `local` funktioniert nur im Simulator bzw. Emulator, auf echten Geräten gegen `development` testen.
- Ab targetSdk 37 (Android 17) braucht `local` Androids Berechtigung für das lokale Netzwerk, um `10.0.2.2` zu erreichen ([Local network permission](https://developer.android.com/privacy-and-security/local-network-permission)). Heute nutzt die App Flutters Standard-targetSdk 36.
- `development` nutzt die Standard-Domain der Container-Apps-Umgebung. Wird die Umgebung neu angelegt, ändert sich die Domain, dann `AppEndpoints` und diese Doku anpassen.
- `production` hat bis zum Release keine Adressen, sie zu lesen wirft `UnimplementedError`. Eine Prüfung beim Start oder Build gibt es bewusst nicht. Dieser Hinweis entfällt, sobald es mit dem Release Werte für `production` gibt.
- `lib/app/core/widgets/flavor_banner.dart`: `FlavorBanner` legt per `MaterialApp.router(builder: ...)` Flutters `Banner` oben links über die App, mit dem Text `local` bzw. `dev`, in `production` nichts.

## Starten und Bauen

In `src/app`:

```bash
fvm flutter run --flavor local
fvm flutter run # development
fvm flutter run --flavor production
fvm flutter build appbundle --flavor development
fvm flutter build ios --flavor development --no-codesign
```

- In Android Studio und IntelliJ starten die geteilten Run-Configurations `App (Flavor: local)`, `App (Flavor: development)` und `App (Flavor: production)` in `.idea/runConfigurations` die App mit dem jeweiligen Flavor.
- Die App per `flutter` starten. Aus Xcode tragen nur die Schemes `local`, `development` und `production` einen Flavor (Flutter liest ihn aus der Configuration), das Scheme `Runner` nicht: Dort käme `appFlavor` aus dem, was die CLI zuletzt in `ios/Flutter/Generated.xcconfig` geschrieben hat, meist ist er leer und `flavorProvider` bricht beim Start mit einem `StateError` ab.
- Android: `productFlavors` `local`, `development` und `production` mit der Dimension `environment` in `android/app/build.gradle.kts`, ohne `applicationIdSuffix`.
- iOS: pro Flavor die Build-Configurations `Debug-<flavor>`, `Release-<flavor>` und `Profile-<flavor>` (Kopien von `Debug`, `Release` und `Profile`, wie es Flutters Doku zu Flavors auf iOS vorgibt) und ein geteiltes Scheme `<flavor>` (Kopie von `Runner.xcscheme` samt der Pre-Action „Run Prepare Flutter Framework Script“ und der LLDB-Init-Datei). `Runner.xcscheme` und die einfachen Configurations `Debug`, `Release` und `Profile` bleiben.
- Flavor-Namen sind klein und ohne `-`: Flutter leitet den Flavor auf iOS aus dem Teil nach dem `-` im Namen der Configuration ab, und `flavorProvider` bildet den Namen per `byName` exakt auf das Enum `Flavor` ab.
- Das Projekt nutzt nur Swift Package Manager. Braucht ein Plugin einmal CocoaPods, muss das Podfile die Flavor-Configurations zuordnen (`'Debug-local' => :debug`, `'Profile-local' => :release`, `'Release-local' => :release`, ebenso für die anderen Flavors).

## Tests

- `test/app/core/flavor/flavor_provider_test.dart` deckt den Standard-Flavor ab und verlässt sich darauf, dass `flutter test` ohne `--flavor` läuft.
- `appFlavor` ist eine Compile-Zeit-Konstante, Tests setzen den Flavor deshalb über `pumpApp(tester, flavor: ...)` bzw. `createContainer(preferences, flavor: ...)`.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Flavor aus `appFlavor` (per `--flavor` oder `default-flavor`) statt `--dart-define` | Ein Schalter für native Konfiguration und Dart, keine zusätzlichen Argumente in Befehlen, IDE und CI. Ein vergessenes `--dart-define` fiele still auf einen Standardwert zurück. |
| `local` über HTTP ohne Ausnahmen in App Transport Security oder Androids Network Security Config | Die App spricht über `dart:io` (Standard-Adapter von dio), das in Flutter keinem von beiden unterliegt: Die Engine setzt `may_insecurely_connect_to_all_domains` fest (Stand Flutter 3.50). Ausnahmen werden erst nötig, wenn lokaler Verkehr über einen nativen HTTP-Stack läuft (WebView, `cupertino_http`, `cronet_http`), und dann nur für `local`. |
| `local` über HTTP statt HTTPS | Geprüft und verworfen (2026-11-17): HTTPS bräuchte TLS in `auth` und der API über Aspires Entwicklerzertifikat (experimentelle API), Vertrauen in jedem Simulator und Emulator pro Entwickler (`dart:io` ignoriert auf Android vom Nutzer installierte CAs), und das Entwicklerzertifikat deckt `10.0.2.2` nicht ab. |
| iOS-Configurations wie in Flutters Doku dupliziert, `Runner` bleibt | Umbenannte Configurations und ein gelöschtes `Runner` brechen `flutter logs` und `flutter create .` und weichen von der Doku ab, der künftige Flutter-Migrationen folgen. |
| Kein Abbruch für `production` beim Start oder Build | Die Werte für `production` kommen mit dem Release, bis dahin wirft nur das Lesen der Adressen. |
| Android-Emulator über `10.0.2.2` statt `adb reverse` | `adb reverse` geht bei jedem Neustart des Emulators verloren. |
