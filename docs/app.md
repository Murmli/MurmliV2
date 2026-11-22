# App

Flutter-App in `src/app`, Pfade relativ dazu. Siehe [apple.md](apple.md), [google-play.md](google-play.md), [ci.md](ci.md#app).

## Flavors

| Flavor | ID (iOS, Android) | Name | API | `auth` |
| --- | --- | --- | --- | --- |
| `local` | `de.murmli.app.dev` | Murmli Dev | `http://localhost:5020`, Android-Emulator `http://10.0.2.2:5020` | `http://localhost:5021`, Android-Emulator `http://10.0.2.2:5021` |
| `development` | `de.murmli.app.dev` | Murmli Dev | `https://api.kinddune-9cab4a57.francecentral.azurecontainerapps.io` | `https://auth.kinddune-9cab4a57.francecentral.azurecontainerapps.io` |
| `production` | `de.murmli.app` | Murmli | – | – |

- Gemeinsame ID: `local` und `development` nicht nebeneinander installierbar, `production` schon.
- `local` braucht `aspire run` ([AGENTS.md](../AGENTS.md#aspire-und-azure)), nur für Simulator bzw. Emulator, Geräte nutzen `development`.
- Ab targetSdk 37 (Android 17, heute Flutters Standard 36) braucht `local` für `10.0.2.2` die [Local network permission](https://developer.android.com/privacy-and-security/local-network-permission).
- `development` nutzt die Standard-Domain der Container-Apps-Umgebung, nach deren Neuanlage `AppEndpoints` und Tabelle anpassen.
- `production` hat bis zum Release weder Adressen noch Google-Client (`_googleServerClientIdOf`), Lesen wirft `UnimplementedError` (gewollt ungeprüft bei Start und Build).
- Flavor-Namen klein und ohne `-`, weil Flutter auf iOS den Teil nach `-` im Configuration-Namen nimmt und `flavorProvider` per `byName` auf `Flavor` abbildet.

## Starten und Bauen

- Xcode-Scheme `Runner` hat keinen Flavor: `appFlavor` käme aus der zuletzt von der CLI geschriebenen, meist leeren `ios/Flutter/Generated.xcconfig` (`flavorProvider` wirft beim Start `StateError`).
- Nur Swift Package Manager. CocoaPods-Plugins bräuchten im Podfile die Zuordnung jeder Flavor-Configuration (`'Debug-local' => :debug`, `'Profile-local' => :release`, `'Release-local' => :release`).

## Icons und Splash

Launcher-Icons und Splash erzeugt [flutter_adaptive_studio](https://pub.dev/packages/flutter_adaptive_studio) aus `flutter_adaptive_studio.yaml`. Die erzeugten Dateien unter `android/` und `ios/` sind committet und werden nie von Hand geändert.

| Flavor | Icon-Hintergrund | Android | iOS |
| --- | --- | --- | --- |
| `production` | `#f57f17` | `src/main/res` (Rückfall für alle Flavors) | `AppIcon` (auch `Debug`, `Release`, `Profile`) |
| `development` | `#1e88e5` | `src/development/res` | `AppIcon-development` |
| `local` | `#e53935` | `src/local/res` | `AppIcon-local` |

- Quellen in `assets/icons/`: `murmli_icon_foreground.svg` (weißer Hamster mit echten Löchern, transparent) für Vordergrund, Monochrom (Android 13), iOS hell und getönt sowie Splash, `murmli_icon_dark_<flavor>.svg` für iOS 18 dunkel. Beide aus `murmli_icon.vectorcraft` (VectorCraft) exportiert, SVG ohne `<style>` (verwirft das Tool still).
- Jeder `<path>` der Quellen bleibt unter 32.767 Zeichen (derzeit Hut, Hutband, Körper samt Löchern): Längere `pathData` ersetzt aapt2 still durch `STRING_TOO_LARGE`, Android zeigt dann für Icon und Splash sein Standard-Icon (Raster mit Android-Kopf). Prüfen per `aapt2 dump xmltree --file res/drawable/ic_launcher_foreground.xml <apk>`.
- Splash: Orange mit weißem Hamster auf Android und iOS, für alle Flavors gleich (Flavor-Einträge mit `splash: null` unter `android` und `ios`).

Neu erzeugen (in `src/app`, nach Änderungen an Config oder Quellen):

```sh
fvm dart run flutter_adaptive_studio doctor
fvm dart run flutter_adaptive_studio generate
fvm dart run flutter_adaptive_studio generate --flavor local
fvm dart run flutter_adaptive_studio generate --flavor development
rm lib/fas_splash.g.dart ios/Runner.xcodeproj/project.pbxproj.bak
```

- dev_dependency mit fester Version (ohne `^`): nur gezielt erhöhen und dann die Ausgabe prüfen, das Paket ändert sich schnell.
- Ohne `--flavor` schreibt das Tool nach `src/main` bzw. `AppIcon`, ein Flavor-Lauf nur in seinen Overlay bzw. `AppIcon-<flavor>` samt `project.pbxproj`.
- `lib/fas_splash.g.dart` (In-App-Splash, ungenutzt) entsteht bei jedem Lauf mit Splash, `project.pbxproj.bak` bei jedem Flavor-Lauf: beide löschen.
- Optional `fvm dart run flutter_adaptive_studio preview` für eine Vorschau der Masken, schreibt `flutter_adaptive_studio/preview/icon_preview.html`, danach den Ordner löschen.
- Fallen Quellen oder Optionen weg, löscht das Tool nicht alles: vorher zurücksetzen (im Repo-Root `git restore --staged --worktree -- src/app/android src/app/ios` und `git clean -fd -- src/app/android src/app/ios`, löscht auch ungetrackte Nicht-Icon-Dateien dort).
- `FileSystemException` mit errno 1224: Ein anderer Prozess (meist VS Code) hält eine erzeugte Datei offen, schließen und wiederholen.
- Neuer Flavor: Eintrag unter `flavors` (mit `play_store: false` und `splash: null` unter `android` und `ios`) und `generate --flavor <flavor>`.

## API-Zugriff

- `AccessTokenInterceptor` setzt das JWT (`SignedInAccount.accessToken`) nur bei Requests an die `origin` der eigenen API. Erneuert wird 1 Minute vor Ablauf, die Gültigkeit (`exp` minus `iat`) zählt ab Empfang. Parallele Requests teilen eine Erneuerung.
- Antwortet die API mit 401, erneuert der Interceptor das JWT und wiederholt den Request einmal. Ohne Session (nie angemeldet oder `auth` lehnt die Session mit 401 ab, dann ist der Nutzer abgemeldet) bricht er Requests ohne Fehlerdialog ab (`DioExceptionType.cancel`). Ist `auth` nicht erreichbar, scheitert der Request mit Fehlerdialog, die Session bleibt.
- Pfade ab `/`, da dio sie als Text an die `baseUrl` (ohne `/` am Ende) hängt: sonst falscher Host (`development`) bzw. ungültiger Port (`local`). Volle URLs ignorieren die `baseUrl`, daher `origin`- statt Instanz-Vergleich.
- `ApiTraceInterceptor` gibt nur Requests an die eigene API einen neuen `traceparent`, auch bei geteilter Instanz. Die API setzt ihn fort ([telemetry.md](telemetry.md#entscheidungen)).
- Request-Logs ohne Bodies (Gesundheitsdaten), Header (Token) und Query (Nutzereingaben). Den `DioException`-Text, den `main.dart` und Sentry melden, ersetzt `ApiTraceInterceptor` durch seine Logzeile: dios Text kann URL samt Query (`HttpException`) oder Body-Teile (`FormatException`) enthalten.
- Fehlerdialog (`ApiErrorDialogListener`): höchstens einer gleichzeitig, keiner bei Abbruch per `CancelToken`, unsichtbarer App oder fehlendem Navigator (auch nicht nachträglich). Aufrufer erhalten den Fehler trotzdem.
- dio ruft auch `onError` in Interceptor-Reihenfolge auf, `handler.resolve` überspringt die folgenden: Als erster loggt `ApiTraceInterceptor` jeden Request, als letzte lässt die Meldung an `failedApiRequestTraceIdProvider` selbst behobene Fehler ohne Dialog.
- `android.permission.INTERNET` im Haupt-Manifest gibt Release-Builds Netz, Flutters Vorlagen-Kopien unter `debug` und `profile` bleiben.

## Anmeldung

Die Onboarding-Seite ist die Anmeldeseite (kein Onboarding-Flag), Optionen je Flavor und Plattform (`SignInOptions`):

- `local`: nur E-Mail und Passwort, Test-Nutzer: [authentication.md](authentication.md#nutzer-anlegen).
- `development`, `production`: Apple nur auf iOS (`SignInWithAppleButton`), Google nur auf Android (eigener Button, [Entscheidungen](#entscheidungen)), kein Formular.

Ablauf: [authentication.md](authentication.md#anmeldung). `signedInAccountProvider` hält das Konto, ohne Konto leitet `SignInGuard` jede geschützte Route zur Onboarding-Seite.

- Apple bekommt den SHA-256 eines zufälligen Nonce, `auth` den rohen.
- Google: nativer Dialog des Button-Ablaufs (`GetSignInWithGoogleOption`, nicht Bottom Sheet), Initialisierung einmal pro Prozess.
- Abbruch im Dialog von Apple oder Google zeigt nichts. Bei Google kommt auch ein falscher SHA-1 oder Paketname (etwa jeder Debug-Build) nach der Kontoauswahl als Abbruch an, für das Plugin ununterscheidbar vom Nutzer-Abbruch ([authentication.md](authentication.md#sign-in-with-google)).
- Die Anmeldung überdauert Neustarts in genau einem Eintrag `session` im Secure Storage (`flutter_secure_storage`) mit Session-Token und Konto-Id. Auf iOS mit `first_unlock_this_device`: Spätere Hintergrundarbeit kann ihn nach dem ersten Entsperren lesen, `_this_device` hält ihn aus Backups. Das JWT liegt nur im Speicher.
- Installationsmarke `installed` in den SharedPreferences: Fehlt sie, leert `restore()` den Secure Storage, weil die iOS-Keychain eine Deinstallation überdauert.
- Android: Der Keystore-Schlüssel kommt nie ins Auto Backup. Nach einer Wiederherstellung löscht `resetOnError` die unlesbaren Daten, der Nutzer ist abgemeldet.
- `main.dart` ruft `restore()` ohne Netz auf, die Session prüft danach eine JWT-Anfrage im Hintergrund. Lehnt `auth` sie mit 401 ab, ist der Nutzer abgemeldet (kurz sichtbare Startseite möglich), andere Fehler lassen ihn angemeldet. So öffnet die App auch offline und während des Kaltstarts von `auth`.
- Ein Listener in `MurmliApp` führt bei jedem Wechsel von angemeldet zu abgemeldet von jeder Seite zur Onboarding-Seite.
- Sign in with Apple braucht ein Gerät mit Apple Account mit Zwei-Faktor-Authentifizierung, im Simulator unzuverlässig (Apple-Feedback FB25318712).
- Offen bis zum Release: `sign_in_with_apple` 8.2.0 scheitert auf dem iPad mit Fehler 1000 ([aboutyou/dart_packages#489](https://github.com/aboutyou/dart_packages/issues/489)).

## Absturzberichte (Sentry)

- Organisation `murmli` (EU, `de.sentry.io`), Projekt `app`. Nur Release- und Profile-Builds von `development` und `production` melden, Environment ist der Flavor. DSN pro Flavor im Code (kein Geheimnis).
- Opt-out: Hinweis im Onboarding, Schalter „Fehlerberichte senden“ unter Einstellungen › Datenschutz.
- Fehler vor `CrashReporting.start()` in `main.dart` (nach Talker-Handlern und SharedPreferences) meldet Sentry nicht.
- `SentryFlutter.init` legt seine Handler vor die aus `main.dart` (ruft sie weiter auf), `Sentry.close()` setzt zurück.
- Ausschalten beendet Sentry samt nativen SDKs sofort und löscht ungesendete Berichte ([Entscheidungen](#entscheidungen)), Einschalten startet es neu.
- Wiedereingeschaltet meldet das Android-SDK den letzten ANR der vergangenen 90 Tage (`ApplicationExitInfo`), auch schon gemeldete oder aus der ausgeschalteten Zeit, da die Marke des zuletzt gemeldeten im gelöschten Cache lag.
- Fehler aus `FlutterError.onError` melden zusätzlich Kontext und Diagnose der `FlutterErrorDetails` (`flutter_error_details`), etwa `NetworkImage("<URL>")` mit voller URL.
- Nutzer: vor der Anmeldung die zufällige Installations-ID der nativen SDKs, danach die eigene Konto-Id, gesetzt von `CrashReporting` bei Änderungen von `signedInAccountProvider` und nach jedem Sentry-Start (`Sentry.close()` verwirft den Scope).
- Sitzungen (Release Health) auch ohne Fehler, neu bei jedem Sentry-Start und ab 30 Sekunden im Hintergrund.
- Support-Code: in Sentry `api_trace_id:<Support-Code>` suchen. Tag von `ApiTraceIdEventProcessor` für `DioException`s der eigenen API, auch in Riverpods `ProviderException`.

## Tests

- `flutter test` läuft im Debug-Modus ohne `--flavor` (für den Test des Standard-Flavors). Tests setzen `flavor` per `pumpApp` oder `createContainer`, `debugBuild` per `createContainer`, in `CrashReporting`-Tests immer, sonst bliebe der Start auch ohne den geprüften Grund aus.
- `createContainer` (auch in `pumpApp`) nutzt Talker ohne Konsolenausgabe (Logs über `history`). Sentry-Meldungen prüft `startRecordingSentry` (Sentry nur in Dart, Transport zeichnet Envelopes auf).
- `RecordingHttpClientAdapter` ersetzt in Tests den Adapter von `apiClientProvider` und `authClientProvider`. Nur der Test auf fehlende Sentry-Trace-Header nutzt dios echten Adapter mit `HttpOverrides` und `HttpServer` auf Loopback, sonst blieben im Adapter gesetzte Header (etwa von `sentry_dio`) unsichtbar.
- Widget-Tests hängen Erwartungen an Requests per `expectLater` vor `tester.pumpAndSettle()` an, warten danach: dios Timer laufen unter `FakeAsync` nur beim Pumpen. Direkt gewartet hängt der Test, danach angehängt wird der Fehler unbehandelt gemeldet.
- `FakeSignInWithApplePlatform` und `FakeGoogleSignInPlatform` (`installFakeGoogleSignIn`) ersetzen die Dialoge von Apple und Google direkt als `instance` der Plattform-Pakete, ohne eigene Hüllen (wären Test-only-Code). `pumpApp(signedInAccount:)` startet angemeldet über den Secure Storage samt `restore()` wie `main.dart` (mit `respondFromAuth` für die Session-Prüfung), `createContainer(signedInAccount:)` setzt nur das Konto. Den Secure Storage ersetzt `createContainer(secureStorage:)` per `FlutterSecureStorage.setMockInitialValues` in jedem Test, die übergebene Map muss veränderlich sein und zeigt das Gespeicherte, für einen Neustart dieselbe Map erneut übergeben. Requests an die API brauchen eine Session: `signIn` (`test/helpers/authentication.dart`) meldet per E-Mail an, danach setzt der Test seinen Adapter. Die Uhr ersetzt `clock` von `createContainer` (`appClockProvider`).
- `flutter test` läuft als Android, plattformspezifische Widget-Tests setzen `variant: TargetPlatformVariant.only(...)`, das auch nach Fehlschlägen zurücksetzt.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Icons und Splash mit `flutter_adaptive_studio` statt `flutter_launcher_icons`, `icons_launcher` oder `flutter_native_splash` | SVG als Quelle mit Safe-Zone-Anpassung, ein 1024er-iOS-Icon samt iOS-18-Varianten, Flavor-Icons samt Xcode-Verdrahtung aus einer Datei. Verworfen (2026-11-21): `flutter_launcher_icons` (seit Juni 2025 kein Release), `icons_launcher` und `flutter_native_splash` (nur PNG). Risiko: junges Paket mit einem Maintainer. |
| `flutter_adaptive_studio` als dev_dependency mit fester Version statt global installiert | Version steht in `pubspec.yaml` und `pubspec.lock` und kommt mit `pub get`, ohne globale Installation, die zudem andere Projekte überschriebe. Dafür lösen seine Abhängigkeiten (`image`, `xml`, `yaml` u. a.) mit denen der App auf. |
| Flavor-Läufe mit `splash: null` und `play_store: false`, Basis mit `round: false` | Flavor-Läufe schrieben sonst den Splash redundant ins Overlay und das Play-Store-Icon nach `src/main` (zuletzt laufender Flavor gewinnt). `round: true` setzt `android:roundIcon` und schreibt dabei das ganze Manifest einzeilig neu, nötig nur für Android 7.1. |
| Flavor per `--flavor` (`appFlavor`) statt `--dart-define` | Ein Schalter für native Konfiguration und Dart, vergessenes `--dart-define` fiele still auf den Standard. |
| `local` über HTTP ohne Ausnahmen in App Transport Security oder Network Security Config | `dart:io` (dios Standard-Adapter) unterliegt keinem, die Engine setzt `may_insecurely_connect_to_all_domains` fest (Stand Flutter 3.50). Ausnahmen erst für nativen HTTP-Stack (WebView, `cupertino_http`, `cronet_http`), nur für `local`. |
| `local` über HTTP statt HTTPS | Verworfen (2026-11-17): bräuchte Aspires Entwicklerzertifikat (experimentelle API) in `auth` und API und Vertrauen in jedem Simulator und Emulator (`dart:io` ignoriert auf Android Nutzer-CAs) und deckte `10.0.2.2` nicht ab. |
| iOS-Configurations nach Flutters Doku dupliziert, `Runner`, `Debug`, `Release`, `Profile` bleiben | Umbenennen oder Löschen bricht `flutter logs` und `flutter create .`, Flutter-Migrationen folgen der Doku. |
| `local` und `development` teilen `de.murmli.app.dev` | CI baut `local` nie, eigene ID kostete App ID und App-Eintrag bei Apple sowie eine Play-Console-App. |
| Android-Emulator über `10.0.2.2` statt `adb reverse` | `adb reverse` geht bei jedem Emulator-Neustart verloren. |
| Keine Timeouts in `apiClientProvider` und `authClientProvider` | API und `auth` skalieren in Dev auf null, ein Timeout müsste den ungemessenen Kaltstart abdecken. Die Anmeldung zeigt bis zur Antwort Fortschritt. |
| `local` nur mit E-Mail und Passwort, direkt auf der Onboarding-Seite | Im Simulator ist Sign in with Apple unzuverlässig, Google bräuchte einen Android-Client pro Debug-Schlüssel jedes Entwicklers. Eine eigene Route kostete pro Start einen Tipp. |
| JWT vor Ablauf erneuern und zusätzlich einmal nach 401 wiederholen | Die Erneuerung vor Ablauf spart den abgelehnten Request, die Wiederholung fängt ab, was sie nicht sieht (etwa ein vorzeitig ungültiges JWT). Verworfen: nur eines von beiden. |
| Gültigkeit des JWT ab Empfang zählen | Eine falsch gehende Geräteuhr verschöbe den Vergleich mit `exp`. Verworfen: `exp` gegen die Geräteuhr. |
| JWT nur im Speicher, nur das Session-Token im Secure Storage | Das JWT läuft nach 15 Minuten ab und lässt sich aus der Session jederzeit neu holen. Verworfen: das JWT mitspeichern. |
| Session-Prüfung nach dem Start im Hintergrund | Die App öffnet ohne Netz und ohne auf den Kaltstart von `auth` zu warten, dafür ist bei abgelaufener Session kurz die Startseite sichtbar. Verworfen: im Splash auf die Prüfung warten. |
| Listener in `MurmliApp` führt nach dem Abmelden zur Onboarding-Seite | Greift bei Abmelden, Löschen und abgelaufener Session auf jeder Seite. Verworfen: `reevaluateListenable` von auto_route, es überspringt bereits aufgelöste Guards und wartet auf Animationen. |
| Installationsmarke leert den Secure Storage nach einer Neuinstallation | Die iOS-Keychain überdauert die Deinstallation. Verworfen: nach einer Neuinstallation auf iOS angemeldet bleiben. |
| Einfacher `Interceptor` mit einer geteilten Erneuerung statt `QueuedInterceptor` | `QueuedInterceptor` reiht alle Requests hintereinander, nicht nur die Erneuerung. |
| Logzeile als Fehler auch bei 401 mit erfolgreicher Wiederholung | Beabsichtigt: `ApiTraceInterceptor` loggt jeden Request vor dem `AccessTokenInterceptor`, so bleibt die Wiederholung im Log sichtbar. Ein Fehlerdialog erscheint nur, wenn auch die Wiederholung scheitert, und dann einmal. |
| Google-Button nach Googles Vorlage statt im Theme-Stil | Die [Branding-Richtlinien](https://developers.google.com/identity/branding-guidelines) schreiben Farben, Schrift und Maße vor. Im Spielraum: Pill wie übrige Buttons statt rechteckig, volle Breite, im Dark Mode „G“ direkt auf dunkler Fläche. Google Sans: offizielle Variable Font aus google/fonts, rund 5 MB, ohne Subsetting. |
| Nur `google_sign_in_android` samt `google_sign_in_platform_interface` statt `google_sign_in` | `google_sign_in` linkte über `google_sign_in_ios` das GoogleSignIn-SDK in die iOS-App (dort nur Apple). Daher direkt `GoogleSignInPlatform.instance`, auf iOS ein nie aufgerufener Platzhalter. Für Google auf iOS ([authentication.md](authentication.md#entscheidungen)) zu `google_sign_in` wechseln, der Test-Fake bleibt. |
| `traceparent` aus eigenem Code statt OpenTelemetry-Bibliothek | Noch kein offizielles OpenTelemetry-SDK für Dart. Spans aus der App: [Murmli/BackOffice#7](https://github.com/Murmli/BackOffice/issues/7). |
| Sampled-Flag `01` | Support-Code führt immer zu einem aufgezeichneten Trace, ohne die API-Sonderregel für `00`. |
| Eigener Interceptor statt `talker_dio_logger` | Loggt (Stand 5.2.0) volle URL samt Query, standardmäßig Bodies, bei Fehlern Header, die Trace-ID nie in Antwort- und Fehler-Logs. |
| Opt-out ohne Einwilligungsdialog, Sitzungen an, Debug-Builds melden nie | [#96](https://github.com/Murmli/MurmliV2/issues/96): Abstürze kommen ohne Zutun an, Crash-free Rate braucht Sitzungen, Entwicklungsfehler gehören nicht in Sentry. |
| Ungesendete Berichte selbst löschen, vor und nach `Sentry.close()` | `sentry_flutter` (Stand 10.5.0) kann gecachte Berichte nicht verwerfen und sendete sie nach dem Wiedereinschalten. Vorher, weil die nativen SDKs beim Beenden bis zu 2 Sekunden senden (iOS alle gecachten), danach, weil sie währenddessen schreiben. Verzeichnisse in `getApplicationCacheDirectory()` laut sentry-java 8.64.0 (`sentry`) und sentry-cocoa 9.35.0 (`io.sentry`, `SentryCrash`). |
| Fehlerdialog global aus `apiClientProvider` statt pro Aufrufer | Jeder fehlgeschlagene Request bekommt ohne Zutun einen Support-Code. |
| Trace-ID als Tag statt in Sentrys Trace-Kontext | Tags sind durchsuchbar, der Trace-Kontext gehört Sentrys Propagation. |
| Kein `sentry_dio` | Meldet fehlgeschlagene Requests selbst, setzt Trace-Header (`sentry-trace`, `baggage`), schreibt Breadcrumbs mit voller URL. |
| `print`- und HTTP-Breadcrumbs aus, Screenshots und View Hierarchy beim Standard (aus) | `print`-Breadcrumbs enthielten Talkers Request-Logs von der Konsole, HTTP-Breadcrumbs die volle URL. |
