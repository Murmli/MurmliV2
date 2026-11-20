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
- Google-Cloud-Projekt „Murmli Google Play“ mit aktivierter Google Play Android Developer API, darin der Service-Account `google-play-upload` ohne IAM-Rollen mit einem JSON-Schlüssel. Neue Google-Cloud-Konten bekommen eine Organisation, deren Standardrichtlinie das Anlegen von Schlüsseln für Service-Accounts verbietet, für dieses Projekt ist die Richtlinie „Disable service account key creation“ überschrieben (braucht die Rolle „Organization Policy Administrator“). Ersetzen siehe [Service-Account-Schlüssel ersetzen](#service-account-schlüssel-ersetzen).
- Der Service-Account ist in der Play Console unter „Users and permissions“ eingeladen, ohne Kontoberechtigungen und mit der App-Berechtigung „Release apps to testing tracks“ nur für „Murmli Dev“. Bis die API neue Berechtigungen annimmt, können bis zu 24 Stunden vergehen.
- `production` hat noch keine App, siehe [`production` bauen](#production-bauen).

## Build in der CI

Der Android-Job von `build-app.yml` läuft auf ubuntu und signiert in jedem Lauf, bei Pull Requests wie bei Push auf `main`. Java 17 kommt per `actions/setup-java`.

1. „Write upload keystore“ schreibt den Keystore aus `ANDROID_UPLOAD_KEYSTORE` nach `$RUNNER_TEMP/upload.keystore`.
2. `flutter build appbundle --release --flavor <flavor> --split-debug-info=build/debug-info --build-number=<github.run_number>` mit den Gradle-Properties `murmliUploadKeystoreFile` (Pfad des Keystores) und `murmliUploadKeystorePassword`, gesetzt per `ORG_GRADLE_PROJECT_murmliUploadKeystoreFile` und `ORG_GRADLE_PROJECT_murmliUploadKeystorePassword`. `src/app/android/app/build.gradle.kts` legt daraus die Signing-Config `upload` (Alias `upload`, Passwort des Schlüssels gleich dem des Keystores) für den Build-Typ `release` an. Fehlen beide Properties (leer zählt als fehlend), signiert Gradle mit dem Debug-Schlüssel, ist nur eine gesetzt, bricht der Build ab. Das App-Bundle landet in `src/app/build/app/outputs/bundle/<flavor>Release`.
3. „Check app bundle“ lädt bundletool in fester Version und prüft dessen SHA-256. Der Step vergleicht den SHA-256-Fingerabdruck des Zertifikats, mit dem das App-Bundle signiert ist (`keytool -printcert -jarfile`), mit dem des Upload-Schlüssels im Keystore und lässt den Job scheitern, wenn sie abweichen, etwa bei einem mit dem Debug-Schlüssel signierten oder unsignierten App-Bundle. Danach liest er per `bundletool dump manifest` App-ID (`package`), `versionName` und `versionCode` aus dem Manifest des App-Bundles und gibt sie als Outputs aus, die App-ID zusätzlich als Output `android-application-id` des Workflows ([Upload nach Google Play](#upload-nach-google-play)).
4. Nur mit dem Input `deliver`: Upload der Symbole nach Sentry mit Release `<App-ID>@<versionName>+<versionCode>` und Dist `<versionCode>` aus „Check app bundle“ ([ci.md](ci.md#app)).
5. Nur mit `deliver`: Der Job legt das App-Bundle als Artifact `android-aab` für einen Tag ab ([Upload nach Google Play](#upload-nach-google-play)).
6. Immer, auch nach Fehlern und Abbrüchen, als letzter Step: „Remove signing secrets“ löscht `upload.keystore` aus `$RUNNER_TEMP`.

- Lokale Release-Builds (`fvm flutter build appbundle`, `fvm flutter run --release`) signieren mit dem Debug-Schlüssel. Wer lokal mit dem Upload-Schlüssel signieren muss, setzt beide Properties in `~/.gradle/gradle.properties`, nie im Repo.

## Upload nach Google Play

`deliver-app.yml` läuft wie für iOS ([apple.md](apple.md#upload-nach-testflight)) nur bei Push auf `main`, nach `lint`, `test`, `build-app` und `vulnerability-scan` und parallel zu `deploy`. Sein Job Android läuft auf ubuntu, Ruby und fastlane kommen wie auf iOS ([apple.md](apple.md#build-in-der-ci)) aus `src/app`.

1. Der Job lädt das Artifact `android-aab` nach `src/app/build/app/outputs/bundle` herunter.
2. `bundle exec fastlane android upload` lädt das App-Bundle per `upload_to_play_store` in den internen Test, mit dem Status `completed` (ohne Einführen in der Play Console) und ohne Metadaten, Versionshinweise, Grafiken und Screenshots, auch falls später `src/app/fastlane/metadata/android` entsteht. Das neue Release ersetzt das bisherige des internen Tests. Den Paketnamen bekommt die Lane per `ANDROID_APPLICATION_ID` aus dem Output `android-application-id` von `build-app.yml`, also aus dem App-Bundle ([Build in der CI](#build-in-der-ci)), das Fastfile kennt keine Zuordnung von Flavor zu Paketname.

fastlane liest den Schlüssel des Service-Accounts aus der Umgebungsvariable `GOOGLE_PLAY_SERVICE_ACCOUNT_KEY`, dekodiert das Base64 im Speicher und übergibt ihn per `json_key_data`. Er landet in keiner Datei, deshalb hat der Job keinen Step „Remove signing secrets“.

Der Job hat die feste Gruppe `google-play-upload` ohne Cancel: Beginnt ein Upload, löscht Google Play die offene Bearbeitung (Edit) desselben Service-Accounts, zwei gleichzeitige Uploads brächen sich gegenseitig ab („This Edit has been deleted“). Wartet in der Gruppe schon ein Upload, bricht GitHub ihn zugunsten des neueren ab. Upload und Deploy nach Dev blockieren sich nicht gegenseitig.

Scheitert der Upload mit „Only releases with status draft may be created on draft app“, siehe [Fehler „draft app“](#fehler-draft-app).

## Secrets

- Repository-Secrets: `ANDROID_UPLOAD_KEYSTORE` (Keystore als Base64) und `ANDROID_UPLOAD_KEYSTORE_PASSWORD` (Passwort von Keystore und Schlüssel).
- Die `ANDROID_*` liegen im Repository, nicht im Environment `development`, aus demselben Grund wie die Apple-Secrets ([apple.md](apple.md#entscheidungen)).
- `GOOGLE_PLAY_SERVICE_ACCOUNT_KEY` (JSON-Schlüssel des Service-Accounts als Base64) liegt wie `SENTRY_AUTH_TOKEN` nur im Environment `development`: Ihn braucht nur der Job Android von `deliver-app.yml`, nur bei Push auf `main`, und das Environment lässt nur `main` zu, Pull Requests kommen also nicht an ihn heran. Der Job nennt das Environment mit `deployment: false`, durchgereicht wird das Secret wie `SENTRY_AUTH_TOKEN` ([ci.md](ci.md#app)).
- `ci.yml` reicht die `ANDROID_*` namentlich an `build-app.yml`, dessen Step „Check Android signing secrets“ vor dem Build prüft, dass keines leer ist. `GOOGLE_PLAY_SERVICE_ACCOUNT_KEY` reicht es nur an `deliver-app.yml`, dessen Job Android ihn vor dem Upload prüft („Check Google Play secret“).

## Build-Nummer

- Android nutzt wie iOS `github.run_number` als Build-Nummer (`versionCode`, [apple.md](apple.md#build-nummer)), Läufe von Pull Requests hinterlassen Lücken. `versionName` ist die Version aus der `pubspec.yaml`.
- Google Play lehnt einen schon verwendeten `versionCode` für immer ab („Version code N has already been used.“). „Re-run all jobs“ nach einem erfolgreichen Upload baut mit demselben `versionCode` und scheitert deshalb wie bei TestFlight beim Upload.
- Nach einem gescheiterten Upload wiederholt „Re-run failed jobs“ ihn mit dem App-Bundle des Laufs, solange das Artifact existiert (ein Tag). Ist es abgelaufen, baut „Re-run all jobs“ neu ([apple.md](apple.md#build-nummer)), der Upload der anderen Plattform scheitert dann, wenn er im ersten Versuch gelungen war.

## Abläufe

### Erster Upload

Google verlangt den ersten Upload jeder App über die Play Console, bis dahin scheitert der Upload aus `deliver-app.yml` für sie.

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

### Service-Account-Schlüssel ersetzen

1. In Google Cloud für den Service-Account `google-play-upload` einen neuen JSON-Schlüssel anlegen.
2. Ihn als Base64 in das Secret `GOOGLE_PLAY_SERVICE_ACCOUNT_KEY` des Environments `development` schreiben: `base64 -i <datei>.json | gh secret set GOOGLE_PLAY_SERVICE_ACCOUNT_KEY --env development --repo Murmli/MurmliV2`.
3. Den alten Schlüssel in Google Cloud löschen.

### Fehler „draft app“

Seit etwa Mai 2026 nimmt Google im internen Test nach Berichten aus der Community auch Releases mit dem Status `completed` von Apps an, die noch nie geprüft wurden, dokumentiert ist das nicht. Scheitert der Upload mit „Only releases with status draft may be created on draft app“:

1. In der Lane `upload` der Plattform `android` im Fastfile `release_status: "draft"` an `upload_to_play_store` übergeben.
2. Bis zur ersten Prüfung der App jedes Release in der Play Console unter „Testen“ > „Interner Test“ einführen.
3. Nach der ersten Prüfung `release_status` wieder entfernen.

### `production` bauen

Noch nicht gebaut, die CI baut nur `development`. Dieser Abschnitt entfällt, sobald sie `production` baut. Dafür braucht es eine App „Murmli“ mit `de.murmli.app` in der Play Console und für den Service-Account die Berechtigung „Release apps to testing tracks“ auch für diese App (beides ein Mensch), ihr erster Upload läuft wie unter [Erster Upload](#erster-upload).

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
| JSON-Schlüssel eines Service-Accounts | Bewusst gewählt, der Weg ist aus anderen Projekten vertraut. Verworfen: Workload Identity Federation, obwohl fastlane 2.242 sie unterstützt. Preis: ein Schlüssel ohne Ablaufdatum im Environment `development`, begrenzt durch die enge Berechtigung in der Play Console. |
| Schlüssel des Service-Accounts im Environment `development` | Ihn braucht nur der Job Android von `deliver-app.yml`, nur bei Push auf `main`, und das Environment lässt nur `main` zu, so kommen Pull Requests nicht an ihn heran. Anders als die `ANDROID_*` und die Apple-Secrets, die Pull Requests zum Signieren brauchen ([apple.md](apple.md#entscheidungen)). |
| Secrets mit Dateiinhalt als Base64 | Keystore, .p12, .p8 und JSON-Schlüssel einheitlich, so muss niemand nachsehen, welches Secret kodiert ist. Auch der JSON-Schlüssel, obwohl `json_key_data` ihn unverändert annähme: fastlane dekodiert ihn im Speicher. |
| Berechtigung „Release apps to testing tracks“ nur für „Murmli Dev“ | fastlane empfiehlt Admin. Mit der engen Berechtigung kann ein geleakter Schlüssel nur in die Test-Tracks der Dev-App veröffentlichen. |
| Paketname aus dem Output `android-application-id` von `build-app.yml` | `upload_to_play_store` liest den Paketnamen nicht aus dem App-Bundle. Verworfen: eine Zuordnung von Flavor zu Paketname im Fastfile wie die Signing-Map auf iOS, sie wäre eine zweite Kopie der App-IDs aus `build.gradle.kts`. |
| Gruppe `google-play-upload` am Android-Job von `deliver-app.yml`, ohne Cancel | Gleichzeitige Uploads löschen sich gegenseitig die Bearbeitung ([Upload nach Google Play](#upload-nach-google-play)), etwa bei zwei schnell aufeinanderfolgenden Merges. Ohne Cancel, damit ein laufender Upload nicht mittendrin abbricht. Keine Gruppe `deploy-dev`, der Upload ändert Azure Dev nicht. |
| Upload auf ubuntu | supply, das hinter `upload_to_play_store` steht, ist reines Ruby und spricht die Play Developer API direkt an, anders als altool auf iOS ([apple.md](apple.md#entscheidungen)). |
