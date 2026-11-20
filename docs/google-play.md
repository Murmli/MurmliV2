# Google Play

Signieren der Android-App und Auslieferung über Google Play. Flavors, App-IDs und Namen in [app.md](app.md#flavors), Sentry-Upload und übrige App-CI in [ci.md](ci.md#app).

## Einrichtung bei Google Play

Alles in diesem Abschnitt legt nur ein Mensch an, ändert oder löscht es.

- Persönliches Entwicklerkonto in der Play Console, für den internen Test reicht es. Entschieden, noch nicht umgesetzt: Vor dem ersten Release in Produktion lässt ein Mensch es in ein Organisationskonto umwandeln, denn persönliche Entwicklerkonten brauchen davor einen geschlossenen Test mit mindestens 12 Testern über 14 Tage, Organisationskonten nicht. Nach der Umwandlung hier „Organisationskonto“ eintragen und diesen Hinweis streichen.
- Ein Upload-Schlüssel (PKCS12-Keystore, Alias `upload`) für alle Apps des Kontos, heute „Murmli Dev“, später auch „Murmli“. Erzeugen und Ersetzen siehe [Upload-Schlüssel erzeugen oder ersetzen](#upload-schlüssel-erzeugen-oder-ersetzen).
- App „Murmli Dev“ mit dem Paketnamen `de.murmli.app.dev`. Der Paketname ist ab dem Anlegen der App für sie registriert und wird nach der ersten Installation nie wieder frei.
- Play App Signing mit einem App-Signaturschlüssel, den Google erzeugt (Standard für neue Apps): Google signiert, was Geräte installieren, der Upload-Schlüssel weist nur Uploads aus.
- Interner Test mit einer E-Mail-Liste (höchstens 100 Tester). Tester treten über den Opt-in-Link bei, beim ersten Mal kann es Stunden dauern, bis er funktioniert.
- Die Einrichtungsaufgaben im Dashboard der Play Console bleiben offen, solange die App nur intern getestet wird: Solange sie offen sind, prüft Google das erste interne Release nicht. Sind sie erledigt, geht das erste Release in die Prüfung (Stunden bis zu sieben Tage).
- `production` hat noch keine App, siehe [`production` bauen](#production-bauen).

## Build in der CI

Der Android-Job von `build-app.yml` läuft auf ubuntu und signiert in jedem Lauf, bei Pull Requests wie bei Push auf `main`. Java 17 kommt per `actions/setup-java`.

1. „Write upload keystore“ schreibt den Keystore aus `ANDROID_UPLOAD_KEYSTORE` nach `$RUNNER_TEMP/upload.keystore`.
2. `flutter build appbundle --release --flavor <flavor> --split-debug-info=build/debug-info --build-number=<github.run_number>` mit den Gradle-Properties `murmliUploadKeystoreFile` (Pfad des Keystores) und `murmliUploadKeystorePassword`, gesetzt per `ORG_GRADLE_PROJECT_murmliUploadKeystoreFile` und `ORG_GRADLE_PROJECT_murmliUploadKeystorePassword`. `src/app/android/app/build.gradle.kts` legt daraus die Signing-Config `upload` (Alias `upload`, Passwort des Schlüssels gleich dem des Keystores) für den Build-Typ `release` an. Fehlen beide Properties (leer zählt als fehlend), signiert Gradle mit dem Debug-Schlüssel, ist nur eine gesetzt, bricht der Build ab. Das App-Bundle landet in `src/app/build/app/outputs/bundle/<flavor>Release`.
3. „Check app bundle“ lädt bundletool in fester Version und prüft dessen SHA-256. Der Step vergleicht den SHA-256-Fingerabdruck des Zertifikats, mit dem das App-Bundle signiert ist (`keytool -printcert -jarfile`), mit dem des Upload-Schlüssels im Keystore und lässt den Job scheitern, wenn sie abweichen, etwa bei einem mit dem Debug-Schlüssel signierten oder unsignierten App-Bundle. Danach liest er per `bundletool dump manifest` App-ID (`package`), `versionName` und `versionCode` aus dem Manifest des App-Bundles und gibt sie als Outputs aus.
4. Nur mit dem Input `deliver`: Upload der Symbole nach Sentry mit Release `<App-ID>@<versionName>+<versionCode>` und Dist `<versionCode>` aus „Check app bundle“ ([ci.md](ci.md#app)).
5. Nur mit `deliver`: Der Job legt das App-Bundle als Artifact `android-aab` für einen Tag ab ([Erster Upload](#erster-upload)).
6. Immer, auch nach Fehlern und Abbrüchen, als letzter Step: „Remove signing secrets“ löscht `upload.keystore` aus `$RUNNER_TEMP`.

- Lokale Release-Builds (`fvm flutter build appbundle`, `fvm flutter run --release`) signieren mit dem Debug-Schlüssel. Wer lokal mit dem Upload-Schlüssel signieren muss, setzt beide Properties in `~/.gradle/gradle.properties`, nie im Repo.

## Upload nach Google Play

Entschieden, noch nicht gebaut: `deliver-app.yml` soll das Artifact `android-aab` wie die IPA ([apple.md](apple.md#upload-nach-testflight)) nach allen Checks per fastlane `upload_to_play_store` mit dem JSON-Schlüssel eines Service-Accounts in den internen Test laden. Dieser Hinweis entfällt, sobald `deliver-app.yml` nach Google Play lädt. Bis dahin lädt ein Mensch hoch, siehe [Erster Upload](#erster-upload).

## Secrets

- Repository-Secrets: `ANDROID_UPLOAD_KEYSTORE` (Keystore als Base64) und `ANDROID_UPLOAD_KEYSTORE_PASSWORD` (Passwort von Keystore und Schlüssel).
- Sie liegen im Repository, nicht im Environment `development`, aus demselben Grund wie die Apple-Secrets ([apple.md](apple.md#entscheidungen)). `SENTRY_AUTH_TOKEN` bleibt im Environment ([ci.md](ci.md#app)).
- `ci.yml` reicht sie namentlich an `build-app.yml`. Dessen Step „Check Android signing secrets“ prüft vor dem Build, dass keines leer ist.

## Build-Nummer

- Android nutzt wie iOS `github.run_number` als Build-Nummer (`versionCode`, [apple.md](apple.md#build-nummer)), Läufe von Pull Requests hinterlassen Lücken. `versionName` ist die Version aus der `pubspec.yaml`.
- Google Play lehnt einen schon hochgeladenen `versionCode` für immer ab. „Re-run all jobs“ eines Laufs, dessen App-Bundle schon hochgeladen ist, baut mit demselben `versionCode`, das neue App-Bundle lässt sich deshalb wie bei TestFlight nicht mehr hochladen.

## Abläufe

### Erster Upload

Google verlangt den ersten Upload jeder App über die Play Console.

1. Aus einem Lauf von `ci.yml` auf `main` innerhalb eines Tages das Artifact `android-aab` herunterladen.
2. In der Play Console in der App unter „Testen“ > „Interner Test“ ein neues Release erstellen.
3. Das App-Bundle hochladen. Fragt die Play Console nach dem App-Signaturschlüssel, den von Google erzeugten wählen.
4. Speichern, das Release prüfen und einführen.
5. Den Testern den Opt-in-Link geben.

Nur ein App-Bundle aus der CI hochladen, nie ein lokal gebautes: Der Schlüssel, mit dem der erste Upload signiert ist, wird zum Upload-Schlüssel der App.

### Upload-Schlüssel erzeugen oder ersetzen

```bash
keytool -genkeypair -v -keystore murmli-upload.keystore -storetype PKCS12 -keyalg RSA -keysize 4096 -validity 10000 -alias upload -dname "CN=Murmli"
```

`ANDROID_UPLOAD_KEYSTORE` auf den Keystore als Base64 setzen, `ANDROID_UPLOAD_KEYSTORE_PASSWORD` auf das Passwort, das `keytool` abfragt.

Ersetzen, etwa nach Verlust oder Kompromittierung, geht nur über Google:

1. Einen neuen Schlüssel mit dem Befehl oben erzeugen.
2. Sein Zertifikat exportieren: `keytool -export -rfc -keystore murmli-upload.keystore -alias upload -file upload_certificate.pem`.
3. In der Play Console in jeder App unter „Play App Signing“ das Zurücksetzen des Upload-Schlüssels beantragen und `upload_certificate.pem` hochladen.
4. Sobald Google das Zurücksetzen bestätigt, die beiden Secrets ersetzen.

Der App-Signaturschlüssel bleibt davon unberührt.

### `production` bauen

Noch nicht gebaut, die CI baut nur `development`. Dieser Abschnitt entfällt, sobald sie `production` baut. Dafür braucht es eine App „Murmli“ mit `de.murmli.app` in der Play Console (ein Mensch), ihr erster Upload läuft wie unter [Erster Upload](#erster-upload).

### bundletool aktualisieren

Version und SHA-256 stehen im Step „Check app bundle“ von `build-app.yml` (`BUNDLETOOL_VERSION`, `BUNDLETOOL_SHA256`). Neueste Version und Digest (ohne das Präfix `sha256:` übernehmen):

```bash
gh api repos/google/bundletool/releases/latest --jq '.tag_name, .assets[].digest'
```

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Signieren über Gradle-Properties aus `ORG_GRADLE_PROJECT_*` | Verworfen: `key.properties` aus Flutters Doku und BoundfoxStudios/Flugwacht. Es bräuchte eine Datei mit dem Passwort auf dem Runner, und das Format von Java-Properties verfälscht Backslashes und Zeichen außerhalb von ISO-8859-1. Derselbe Weg wie bei `sentryUploadProguardMapping` ([ci.md](ci.md#app)). |
| Debug-Schlüssel ohne Properties | `flutter run --release` und lokale Release-Builds funktionieren ohne Upload-Schlüssel. Die CI prüft die Secrets vor und die Signatur nach dem Build, ist nur eine Property gesetzt, bricht der Build ab, statt still auf den Debug-Schlüssel zurückzufallen. |
| Ein Upload-Schlüssel für alle Apps | Wie das eine Zertifikat „Apple Distribution“ ([apple.md](apple.md#einrichtung-bei-apple)). Der Schlüssel allein liefert kein Update aus, dafür braucht es Zugang zur Play Console, und Google kann ihn zurücksetzen. |
| Signieren auch in Pull Requests | Fehler beim Signieren fallen im Pull Request auf statt erst nach dem Merge auf `main`. |
| App-Name per `manifestPlaceholders` | Verworfen: `resValue`, es braucht `buildFeatures.resValues`, das AGP 9 standardmäßig abschaltet; eine `strings.xml` pro Flavor wie in Flutters Doku, `local` und `development` bräuchten je eine Kopie. So stehen App-ID und Name in einem Block pro Flavor, wie `APP_DISPLAY_NAME` pro Configuration auf iOS. Der Platzhalter heißt `appLabel`, `applicationName` gehört dem Flutter Gradle Plugin. |
| Release und Dist per bundletool aus dem gebauten App-Bundle | Wie die `Info.plist` auf iOS, ohne zweite Kopie der Zuordnung von Flavor zu App-ID in der CI. Die Manifeste unter `build/app/intermediates` sind kein öffentlicher Vertrag von AGP. bundletool ist auf den Runnern nicht vorinstalliert, deshalb feste Version mit SHA-256. |
| Erster Upload von Hand | Googles Doku zur Play Developer API verlangt, dass der erste Upload einer App über die Play Console läuft. |
| App-Bundle als Artifact im öffentlichen Repo | Angemeldete GitHub-Nutzer können es herunterladen, das ist hingenommen: Es enthält keine Secrets und nichts, was sich nicht aus dem öffentlichen Code ergibt (auch das eingebettete R8-Mapping nicht), ist ohne Dart-Symbole gebaut (`--split-debug-info`) und verfällt nach einem Tag. |
