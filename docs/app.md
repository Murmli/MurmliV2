# App

Flutter-App in `src/app` (Pfade unten relativ dazu). Signieren und Auslieferung in [apple.md](apple.md) und [google-play.md](google-play.md), CI und Symbol-Upload in [ci.md](ci.md#app).

## Flavors

| Flavor | ID (iOS und Android) | Name | API | Anmeldung (`auth`) |
| --- | --- | --- | --- | --- |
| `local` | `de.murmli.app.dev` | Murmli Dev | `http://localhost:5020`, im Android-Emulator `http://10.0.2.2:5020` | `http://localhost:5021`, im Android-Emulator `http://10.0.2.2:5021` |
| `development` | `de.murmli.app.dev` | Murmli Dev | `https://api.kinddune-9cab4a57.francecentral.azurecontainerapps.io` | `https://auth.kinddune-9cab4a57.francecentral.azurecontainerapps.io` |
| `production` | `de.murmli.app` | Murmli | noch keine | noch keine |

- Wegen der gemeinsamen ID lassen sich `local` und `development` nicht nebeneinander installieren, `production` neben ihnen schon.
- `local` braucht ein laufendes `aspire run` ([AGENTS.md](../AGENTS.md#aspire-und-azure)) und läuft nur im Simulator bzw. Emulator, auf echten Geräten gegen `development` testen.
- Ab targetSdk 37 (Android 17) braucht `local` für `10.0.2.2` die Berechtigung für das lokale Netzwerk ([Local network permission](https://developer.android.com/privacy-and-security/local-network-permission)). Heute gilt Flutters Standard-targetSdk 36.
- `development` nutzt die Standard-Domain der Container-Apps-Umgebung. Wird die Umgebung neu angelegt, ändert sie sich, dann `AppEndpoints` und diese Tabelle anpassen.
- `production` hat bis zum Release keine Adressen und keinen Google-Client ([Anmeldung](#anmeldung)), beides zu lesen wirft `UnimplementedError`, bewusst ohne Prüfung beim Start oder Build. Entfällt, sobald es mit dem Release Werte gibt.
- Flavor-Namen sind klein und ohne `-`, weil Flutter den Flavor auf iOS aus dem Teil nach dem `-` im Namen der Configuration ableitet und `flavorProvider` den Namen per `byName` auf `Flavor` abbildet.

## Starten und Bauen

- Das Xcode-Scheme `Runner` trägt keinen Flavor: `appFlavor` käme aus der zuletzt von der CLI geschriebenen `ios/Flutter/Generated.xcconfig`, meist leer, dann bricht `flavorProvider` beim Start mit `StateError` ab.
- Das Projekt nutzt nur Swift Package Manager. Braucht ein Plugin CocoaPods, muss das Podfile die Flavor-Configurations zuordnen (`'Debug-local' => :debug`, `'Profile-local' => :release`, `'Release-local' => :release`, ebenso für die anderen Flavors).

## API-Zugriff

- Der erste Aufruf ist `GET /v1/users/me` bei der [Anmeldung](#anmeldung), das JWT setzt er ausdrücklich am Request. Entschieden, noch nicht gebaut: ein Interceptor für alle Requests mit [#54](https://github.com/Murmli/MurmliV2/issues/54), dann entfällt dieser Punkt.
- Pfade beginnen mit `/`, weil dio den Pfad als Text an die `baseUrl` (ohne `/` am Ende) hängt: Sonst entstünde bei `development` ein falscher Host, bei `local` ein ungültiger Port. Eine volle URL ignoriert die `baseUrl`, deshalb prüft Code für die eigene API die `origin` statt der Instanz.
- `ApiTraceInterceptor` gibt nur Requests an die eigene API einen neuen `traceparent` mit, `auth` und Drittanbieter bekommen keinen, auch über dieselbe Instanz. Die API setzt den Trace fort ([telemetry.md](telemetry.md#entscheidungen)).
- Request-Logs ohne Bodies (Gesundheitsdaten), Header (Token) und Query (Nutzereingaben). Auch den Text der `DioException` ersetzt `ApiTraceInterceptor` durch seine Logzeile, denn `main.dart` loggt und Sentry meldet ihn, und dios Standardtext enthält etwa eine `HttpException` mit voller URL samt Query oder eine `FormatException` mit Teilen des Bodies.
- Fehlerdialog (`ApiErrorDialogListener`): nicht bei Abbruch per `CancelToken`, höchstens einer gleichzeitig, keiner (auch nicht nachträglich), solange die App nicht sichtbar ist oder der Navigator fehlt. Der Aufrufer bekommt den Fehler trotzdem.
- dio ruft auch `onError` in Reihenfolge der Interceptors auf, `handler.resolve` überspringt die folgenden. Deshalb bleibt mit `ApiTraceInterceptor` zuerst jeder Request im Log, und mit der Meldung an `failedApiRequestTraceIdProvider` zuletzt löst ein selbst behobener Fehler keinen Dialog aus.
- `android.permission.INTERNET` im Haupt-Manifest gibt Release-Builds Netz. Die gleichen Einträge unter `debug` und `profile` stammen aus Flutters Vorlage und bleiben.

## Anmeldung

Welche Anmeldung die Onboarding-Seite zeigt, hängt von Flavor und Plattform ab (`SignInOptions`):

- `local`: auf iOS und Android nur das Formular für E-Mail und Passwort direkt auf der Onboarding-Seite, weder Apple noch Google. Test-Nutzer für `aspire run`: [authentication.md](authentication.md#nutzer-anlegen).
- `development` und `production`: Apple nur auf iOS, per `SignInWithAppleButton` des Pakets nach Apples HIG (schwarz, im Dark Mode weiß, Titel „Mit Apple fortfahren“), Google nur auf Android, als `OutlinedButton` mit eigenem Stil genau nach Googles [Branding-Richtlinien](https://developers.google.com/identity/branding-guidelines) mit dem offiziellen „G“ (`assets/images/google_logo.png`) und dem Titel „Weiter mit Google“ in Google Sans Medium (mitgeliefert in `assets/fonts`), hell oder dunkel nach der Helligkeit der App ([Entscheidungen](#entscheidungen)), nie das Formular.

Ablauf (Endpunkte in [authentication.md](authentication.md#anmeldung)):

1. Anmelden bei `auth` über `authClientProvider`, eine eigene dio-Instanz ohne die Interceptors der API:
   - Apple (`signInWithApple`): Apple-Dialog ohne Scopes mit dem SHA-256 eines zufälligen Nonce, dann Identity-Token und roher Nonce an `auth`.
   - Google (`signInWithGoogle`): `google_sign_in_android` zeigt über den Credential Manager Googles nativen Dialog des Button-Ablaufs (`GetSignInWithGoogleOption`, nicht das Bottom Sheet), ohne Nonce, dann das ID-Token an `auth`. Die `serverClientId` (Web-Client-ID, [authentication.md](authentication.md#sign-in-with-google)) steht pro Flavor im Switch `_googleServerClientIdOf` in `signed_in_account_provider.dart`, `production` wirft dort bis zum Release ([Flavors](#flavors)). Die Initialisierung läuft einmal pro Prozess.
   - E-Mail und Passwort (`signInWithEmail`): beide an `auth`.
2. Mit dem Session-Token aus `set-auth-token` das JWT holen, mit diesem `GET /v1/users/me` aufrufen.
3. `signedInAccountProvider` hält das Konto. Solange keins angemeldet ist, schickt `SignInGuard` jede geschützte Route auf die Onboarding-Seite.

- Abbruch im Dialog von Apple oder Google zeigt nichts, 401 oder 400 auf die Anmeldung mit E-Mail und Passwort (`invalidCredentials`) die Snackbar zu falschen Anmeldedaten, ein anderer Fehler bei Apple, Google oder `auth` die allgemeine Snackbar, ein Fehler der API nur den globalen Fehlerdialog.
- Ein falscher SHA-1 oder Paketname im Android-Client bei Google kommt nach der Kontoauswahl als Abbruch an (für das Plugin nicht von einem Abbruch durch den Nutzer zu unterscheiden), es passiert also nichts. Das gilt auch für Debug-Builds ([authentication.md](authentication.md#sign-in-with-google)).
- Bis [#54](https://github.com/Murmli/MurmliV2/issues/54) liegt nur das Konto im Speicher, nach einem Neustart meldet man sich erneut an, dann entfällt dieser Punkt.
- Die Onboarding-Seite ist die Anmeldeseite, ein eigenes Onboarding-Flag gibt es nicht.
- Sign in with Apple braucht ein Gerät mit Apple Account mit Zwei-Faktor-Authentifizierung. Der Simulator ist derzeit unzuverlässig (Apple-Feedback FB25318712), auf einem Gerät testen.
- Offen bis zum Release: `sign_in_with_apple` 8.2.0 scheitert auf dem iPad mit Fehler 1000 ([aboutyou/dart_packages#489](https://github.com/aboutyou/dart_packages/issues/489)).

## Absturzberichte (Sentry)

- Sentry-Organisation `murmli` in der EU-Region (`de.sentry.io`), Projekt `app`. Es melden nur Release- und Profile-Builds von `development` und `production`, mit dem Flavor-Namen als Environment. Der DSN steht pro Flavor im Code, er ist kein Geheimnis.
- Opt-out: Hinweis im Onboarding, Schalter „Fehlerberichte senden“ in den Einstellungen im Bereich „Datenschutz“.
- Fehler vor `CrashReporting.start()` in `main.dart` (nach Talker-Handlern und SharedPreferences) meldet Sentry nicht.
- `SentryFlutter.init` legt seine Handler vor die aus `main.dart` und ruft diese weiter auf, `Sentry.close()` setzt sie zurück.
- Ausschalten beendet Sentry samt nativen SDKs sofort und löscht deren ungesendete Berichte ([Entscheidungen](#entscheidungen)), Einschalten startet es wieder.
- Nach dem Wiedereinschalten meldet das Android-SDK den letzten ANR der vergangenen 90 Tage (`ApplicationExitInfo`), auch einen aus der Zeit ohne Meldungen oder einen schon gemeldeten: Die Marke des zuletzt gemeldeten ANR lag im gelöschten Cache.
- Bei Fehlern aus `FlutterError.onError` gehen Kontext und Diagnose der `FlutterErrorDetails` mit (`flutter_error_details`), etwa `NetworkImage("<URL>")` mit voller URL.
- Nutzer ist vor der Anmeldung die zufällige Installations-ID der nativen SDKs, danach die eigene Konto-Id. `CrashReporting` setzt sie per `CrashReporter.setUserId` bei jeder Änderung von `signedInAccountProvider` (bei `null` entfernt es sie) und nach jedem Start von Sentry (`Sentry.close()` verwirft den Scope).
- Sitzungen (Release Health) gehen auch ohne Fehler an Sentry, eine neue bei jedem Start von Sentry und nach mindestens 30 Sekunden im Hintergrund.
- Support-Code finden: in Sentry nach `api_trace_id:<Support-Code>` suchen. Das Tag setzt `ApiTraceIdEventProcessor` für `DioException`s der eigenen API, auch in Riverpods `ProviderException` verpackt.

## Tests

- `flutter test` läuft ohne `--flavor` (der Test des Standard-Flavors verlässt sich darauf) und im Debug-Modus. Tests setzen den Flavor über `pumpApp` bzw. `createContainer` (`flavor`), den Build-Modus über `createContainer` (`debugBuild`). Tests von `CrashReporting` setzen `debugBuild` ausdrücklich, sonst bliebe der Start auch ohne den geprüften Grund aus.
- `createContainer` (und damit `pumpApp`) setzt einen Talker ohne Konsolenausgabe ein (Logs über `history` prüfen). Was an Sentry geht, prüft `startRecordingSentry`: Sentry rein in Dart mit einem Transport, der Envelopes aufzeichnet.
- Tests ersetzen den Adapter der Instanzen aus `apiClientProvider` und `authClientProvider` durch `RecordingHttpClientAdapter`. Nur der Test, dass Requests keine Trace-Header von Sentry tragen, behält dios echten Adapter und leitet per `HttpOverrides` an einen `HttpServer` auf Loopback: Header, die eine Sentry-Integration wie `sentry_dio` im Adapter setzt, sähe `RecordingHttpClientAdapter` nicht.
- Widget-Tests hängen die Erwartung an einen Request per `expectLater` vor `tester.pumpAndSettle()` an und warten erst danach auf sie: dio arbeitet über Timer, die unter `FakeAsync` nur beim Pumpen laufen. Direkt gewartet hängt der Test, eine erst nach dem Pumpen angehängte Erwartung meldet den Fehler als unbehandelt.
- Den Apple-Dialog ersetzt `FakeSignInWithApplePlatform` über `SignInWithApplePlatform.instance`, den von Google `FakeGoogleSignInPlatform` über `GoogleSignInPlatform.instance` (`installFakeGoogleSignIn` in `test/helpers/google_sign_in.dart` setzt nach dem Test die vorige Instanz zurück). Angemeldet starten Tests über `signedInAccount` von `createContainer` bzw. `pumpApp`.
- `flutter test` läuft als Android. Widget-Tests für eine Plattform setzen sie per `variant: TargetPlatformVariant.only(...)`, das `debugDefaultTargetPlatformOverride` setzt und auch nach einem Fehlschlag zurücksetzt.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Flavor per `--flavor` (`appFlavor`) statt `--dart-define` | Ein Schalter für native Konfiguration und Dart. Ein vergessenes `--dart-define` fiele still auf einen Standardwert zurück. |
| `local` über HTTP ohne Ausnahmen in App Transport Security oder Network Security Config | `dart:io` (dios Standard-Adapter) unterliegt keinem von beiden, die Engine setzt `may_insecurely_connect_to_all_domains` fest (Stand Flutter 3.50). Ausnahmen erst für einen nativen HTTP-Stack (WebView, `cupertino_http`, `cronet_http`), dann nur für `local`. |
| `local` über HTTP statt HTTPS | Verworfen (2026-11-17): bräuchte TLS in `auth` und API über Aspires Entwicklerzertifikat (experimentelle API), Vertrauen in jedem Simulator und Emulator (`dart:io` ignoriert auf Android vom Nutzer installierte CAs), und das Zertifikat deckt `10.0.2.2` nicht ab. |
| iOS-Configurations wie in Flutters Doku dupliziert, `Runner` und `Debug`, `Release`, `Profile` bleiben | Umbenennen oder Löschen bricht `flutter logs` und `flutter create .` und weicht von der Doku ab, der künftige Flutter-Migrationen folgen. |
| `local` und `development` teilen `de.murmli.app.dev` | Die Pipeline baut `local` nie, eine eigene ID kostete App ID und App-Eintrag bei Apple und eine App in der Play Console. |
| Android-Emulator über `10.0.2.2` statt `adb reverse` | `adb reverse` geht bei jedem Neustart des Emulators verloren. |
| Keine Timeouts in `apiClientProvider` und `authClientProvider` | API und `auth` skalieren in Dev auf null, ein Antwort-Timeout müsste den Kaltstart abdecken, gemessen ist noch keiner. Die Anmeldung zeigt ihren Fortschritt, bis eine Antwort kommt. |
| Im Flavor `local` nur E-Mail und Passwort, sonst nur Apple bzw. Google | `local` läuft nur im Simulator bzw. Emulator ([Flavors](#flavors)), dort ist Sign in with Apple unzuverlässig, und Google bräuchte im Google-Cloud-Projekt einen Android-Client für den Debug-Schlüssel jedes Entwicklers. Apple nur auf iOS, Google nur auf Android siehe [authentication.md](authentication.md#entscheidungen). |
| Google-Button nach Googles Vorlage statt im Stil des Themes | Googles Branding-Richtlinien schreiben Farben, Schrift, Maße und Abstände vor. Bewusste Ausnahme von der Theme-Regel (Skill `flutter-conventions`). Pill wie die übrigen Buttons der App (die Richtlinien erlauben auch rechteckig), volle Breite mit zentriertem Logo und Titel (die Richtlinien regeln die Breite nicht), sichtbar mindestens 40 hoch, Tap-Ziel 48. Im Dark Mode steht das „G“ direkt auf der dunklen Fläche, wie Googles Vorlagen und die Do/Don't-Liste der Richtlinien es erlauben. Google Sans liegt als offizielle Variable Font aus google/fonts bei (rund 5 MB, ohne Subsetting). |
| Formular direkt auf der Onboarding-Seite statt eigener Route | In `local` gibt es nichts anderes zu wählen, eine eigene Route kostete bei jedem Start einen Tipp mehr. |
| Fakes über `SignInWithApplePlatform` und `GoogleSignInPlatform` statt eigener Hüllen | Eine Hülle nur für Tests wäre Test-only-Code in Produktion. |
| Nur `google_sign_in_android` samt `google_sign_in_platform_interface` statt `google_sign_in` | `google_sign_in` bringt `google_sign_in_ios` mit, das das GoogleSignIn-SDK in die iOS-App linkt, obwohl iOS nur Apple anbietet. Die App ruft deshalb `GoogleSignInPlatform.instance` direkt auf, auf iOS bleibt dort der nie aufgerufene Platzhalter. Bietet die App Google später auch auf iOS an, wechselt die Abhängigkeit zu `google_sign_in`, und die beiden Aufrufe werden `GoogleSignIn.instance.initialize` bzw. `authenticate`. Der Test-Fake bleibt, weil auch `google_sign_in` an `GoogleSignInPlatform.instance` delegiert. iOS braucht dann zusätzlich einen iOS-OAuth-Client sowie `GIDClientID` und ein URL-Schema in der `Info.plist`. |
| `traceparent` aus eigenem Code statt einer OpenTelemetry-Bibliothek | Ein offizielles OpenTelemetry-SDK für Dart gibt es noch nicht. Spans aus der App gehören zu [Murmli/BackOffice#7](https://github.com/Murmli/BackOffice/issues/7). |
| Sampled-Flag `01` | Der Support-Code soll immer auf einen aufgezeichneten Trace führen, ohne von der Sonderregel der API für `00` abzuhängen. |
| Eigener Interceptor statt `talker_dio_logger` | `talker_dio_logger` (Stand 5.2.0) loggt die volle URL samt Query, standardmäßig Bodies und bei Fehlern Header, die Trace-ID nie in Antwort- und Fehler-Logs. |
| Opt-out ohne Einwilligungsdialog, Sitzungen an, Debug-Builds melden nie | Festgelegt in [#96](https://github.com/Murmli/MurmliV2/issues/96): Abstürze sollen ohne Zutun ankommen, die Crash-free Rate braucht Sitzungen, beim Entwickeln ausgelöste Fehler gehören nicht in Sentry. |
| Ungesendete Berichte selbst löschen, vor und nach `Sentry.close()` | `sentry_flutter` (Stand 10.5.0) kann gecachte Berichte nicht verwerfen, sonst sendete es sie nach dem Wiedereinschalten. Vorher, weil die nativen SDKs beim Beenden bis zu 2 Sekunden senden (iOS alle gecachten Berichte), danach, weil sie dabei noch schreiben. Verzeichnisse in `getApplicationCacheDirectory()` laut sentry-java 8.64.0 (`sentry`) und sentry-cocoa 9.35.0 (`io.sentry`, `SentryCrash`), bei Updates von `sentry_flutter` prüfen. |
| Fehlerdialog global aus `apiClientProvider` statt pro Aufrufer | Jeder fehlgeschlagene Request bekommt einen Support-Code, ohne dass ein Aufrufer daran denken muss. |
| Trace-ID als Tag statt im Trace-Kontext von Sentry | Tags sind durchsuchbar, der Trace-Kontext gehört Sentrys eigener Propagation. |
| Kein `sentry_dio` | Es meldet fehlgeschlagene Requests selbst, setzt eigene Trace-Header (`sentry-trace`, `baggage`) und schreibt Breadcrumbs mit voller URL. |
| `print`- und HTTP-Breadcrumbs aus, Screenshots und View Hierarchy beim Standard (aus) | `print`-Breadcrumbs enthielten die Request-Logs, die Talker auf die Konsole schreibt, HTTP-Breadcrumbs die volle URL. |
