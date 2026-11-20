# Google Play

Signieren der Android-App und Auslieferung über Google Play. Flavors und App-IDs in [app.md](app.md#flavors), Sentry-Upload in [ci.md](ci.md#app).

## Einrichtung bei Google Play

- Persönliches Entwicklerkonto in der Play Console. Entschieden, noch nicht umgesetzt: vor dem ersten Release in Produktion in ein Organisationskonto umwandeln lassen, denn persönliche Konten brauchen davor einen geschlossenen Test mit mindestens 12 Testern über 14 Tage. Danach hier „Organisationskonto“ eintragen und den Hinweis streichen.
- Ein Upload-Schlüssel (PKCS12, Alias `upload`) für alle Apps des Kontos ([erzeugen oder ersetzen](#upload-schlüssel-erzeugen-oder-ersetzen)).
- App „Murmli Dev“ mit `de.murmli.app.dev`. Der Paketname ist ab dem Anlegen an sie vergeben und wird nach der ersten Installation nie wieder frei.
- Play App Signing mit einem von Google erzeugten App-Signaturschlüssel, der Upload-Schlüssel weist nur Uploads aus.
- Interner Test mit E-Mail-Liste (höchstens 100 Tester). Der Opt-in-Link funktioniert beim ersten Mal unter Umständen erst nach Stunden.
- Die Einrichtungsaufgaben im Dashboard bleiben offen, solange die App nur intern getestet wird: Dann prüft Google das erste interne Release nicht. Sind sie erledigt, geht das erste Release in die Prüfung (Stunden bis sieben Tage).
- Google-Cloud-Projekt „Murmli Google Play“ mit der Google Play Android Developer API und dem Service-Account `google-play-upload` ohne IAM-Rollen, mit JSON-Schlüssel ([ersetzen](#service-account-schlüssel-ersetzen)). Die Standardrichtlinie neuer Google-Cloud-Organisationen verbietet solche Schlüssel, für dieses Projekt ist „Disable service account key creation“ überschrieben (braucht die Rolle „Organization Policy Administrator“).
- Der Service-Account ist in der Play Console unter „Users and permissions“ eingeladen, ohne Kontoberechtigungen, mit der App-Berechtigung „Release apps to testing tracks“ nur für „Murmli Dev“. Neue Berechtigungen greifen in der API erst nach bis zu 24 Stunden.
- `production`: [`production` bauen](#production-bauen).

## Build in der CI

- Gradle signiert `release` mit dem Upload-Schlüssel nur, wenn die Properties `murmliUploadKeystoreFile` und `murmliUploadKeystorePassword` gesetzt sind (leer zählt als fehlend). Ohne beide signiert es mit dem Debug-Schlüssel, mit nur einer bricht der Build ab. Lokal mit dem Upload-Schlüssel signieren: beide in `~/.gradle/gradle.properties` setzen, nie im Repo.
- „Check app bundle“ vergleicht den SHA-256-Fingerabdruck des Zertifikats im App-Bundle mit dem des Upload-Schlüssels und liest per `bundletool dump manifest` `package`, `versionName` und `versionCode` für Release und Dist in Sentry und den Output `android-application-id`.
- Mit `deliver`: App-Bundle als Artifact `android-aab` für einen Tag.

## Upload nach Google Play

`deliver-app.yml` lädt `android-aab` per `upload_to_play_store` in den internen Test, mit dem Status `completed` (ohne Einführen in der Play Console) und ohne Metadaten. Das neue Release ersetzt das bisherige. Zeitpunkt wie auf iOS ([apple.md](apple.md#entscheidungen)), gleichzeitige Uploads unter [Entscheidungen](#entscheidungen). Fehler „Only releases with status draft may be created on draft app“: [Fehler „draft app“](#fehler-draft-app).

## Secrets

- Repository (Grund wie bei Apple, [apple.md](apple.md#entscheidungen)): `ANDROID_UPLOAD_KEYSTORE` (Keystore, Base64), `ANDROID_UPLOAD_KEYSTORE_PASSWORD` (für Keystore und Schlüssel).
- Environment `development` ([Entscheidungen](#entscheidungen)): `GOOGLE_PLAY_SERVICE_ACCOUNT_KEY` (JSON-Schlüssel des Service-Accounts, Base64).

## Build-Nummer

- `versionCode` ist wie auf iOS `github.run_number`, Lücken und Re-runs wie dort ([apple.md](apple.md#build-nummer)). `versionName` ist die Version aus der `pubspec.yaml`.
- Google Play lehnt einen schon verwendeten `versionCode` für immer ab („Version code N has already been used.“).

## Abläufe

### Erster Upload

Google verlangt den ersten Upload jeder App über die Play Console (Doku der Play Developer API), bis dahin scheitert `deliver-app.yml` für sie.

1. Das Artifact `android-aab` aus einem höchstens einen Tag alten Lauf von `ci.yml` auf `main` laden. Nie ein lokal gebautes App-Bundle: Der Schlüssel des ersten Uploads wird zum Upload-Schlüssel der App.
2. Play Console, „Testen“ > „Interner Test“: neues Release, App-Bundle hochladen, beim App-Signaturschlüssel den von Google erzeugten wählen.
3. Speichern, prüfen, einführen, den Testern den Opt-in-Link geben.

### Upload-Schlüssel erzeugen oder ersetzen

```bash
keytool -genkeypair -v -keystore murmli-upload.keystore -storetype PKCS12 -keyalg RSA -keysize 4096 -validity 10000 -alias upload -dname "CN=Murmli"
```

`ANDROID_UPLOAD_KEYSTORE` auf den Keystore als Base64 setzen, `ANDROID_UPLOAD_KEYSTORE_PASSWORD` auf das abgefragte Passwort.

Ersetzen geht nur über Google, der App-Signaturschlüssel bleibt unberührt:

1. Neuen Schlüssel wie oben erzeugen, Zertifikat exportieren: `keytool -export -rfc -keystore murmli-upload.keystore -alias upload -file upload_certificate.pem`.
2. In jeder App unter „Play App Signing“ das Zurücksetzen des Upload-Schlüssels beantragen und `upload_certificate.pem` hochladen.
3. Nach Googles Bestätigung beide Secrets ersetzen.

### Service-Account-Schlüssel ersetzen

1. In Google Cloud für `google-play-upload` einen neuen JSON-Schlüssel anlegen.
2. `base64 -i <datei>.json | gh secret set GOOGLE_PLAY_SERVICE_ACCOUNT_KEY --env development --repo Murmli/MurmliV2`
3. Den alten Schlüssel löschen.

### Fehler „draft app“

Undokumentiert, nach Berichten aus der Community seit etwa Mai 2026: Google nimmt im internen Test auch Releases mit dem Status `completed` von nie geprüften Apps an. Scheitert der Upload trotzdem mit „Only releases with status draft may be created on draft app“:

1. In der Lane `android upload` des Fastfile `release_status: "draft"` an `upload_to_play_store` übergeben.
2. Bis zur ersten Prüfung der App jedes Release in der Play Console einführen.
3. Danach `release_status` wieder entfernen.

### `production` bauen

Noch nicht gebaut, die CI baut nur `development` (Abschnitt entfällt, sobald sie `production` baut). Es fehlen (ein Mensch) die App „Murmli“ mit `de.murmli.app` in der Play Console und „Release apps to testing tracks“ auch dafür für den Service-Account, danach der [erste Upload](#erster-upload).

### bundletool aktualisieren

`BUNDLETOOL_VERSION` und `BUNDLETOOL_SHA256` im Step „Check app bundle“ von `build-app.yml` setzen, den Digest ohne `sha256:`:

```bash
gh api repos/google/bundletool/releases/latest --jq '.tag_name, .assets[].digest'
```

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Signieren über Gradle-Properties aus `ORG_GRADLE_PROJECT_*` | Verworfen: `key.properties` (Flutters Doku, BoundfoxStudios/Flugwacht). Bräuchte eine Datei mit dem Passwort auf dem Runner, und Java-Properties verfälschen Backslashes und Zeichen außerhalb von ISO-8859-1. |
| Debug-Schlüssel ohne Properties | Lokale Release-Builds laufen ohne Upload-Schlüssel. Die CI fällt nie still darauf zurück, sie prüft die Secrets vor und die Signatur nach dem Build. |
| Ein Upload-Schlüssel für alle Apps | Wie das eine Zertifikat auf iOS. Allein liefert er kein Update aus (dafür braucht es die Play Console), und Google kann ihn zurücksetzen. |
| Signieren auch in Pull Requests | Wie auf iOS ([apple.md](apple.md#entscheidungen)). |
| App-Name per `manifestPlaceholders` | Verworfen: `resValue` (braucht `buildFeatures.resValues`, in AGP 9 standardmäßig aus) und eine `strings.xml` pro Flavor (je eine Kopie für `local` und `development`). Der Platzhalter heißt `appLabel`, weil `applicationName` dem Flutter Gradle Plugin gehört. |
| Release, Dist und Paketname per bundletool aus dem App-Bundle | `upload_to_play_store` liest den Paketnamen nicht aus dem App-Bundle. Verworfen: Manifeste unter `build/app/intermediates` (kein öffentlicher Vertrag von AGP) und eine Zuordnung von Flavor zu App-ID in CI oder Fastfile (zweite Kopie der App-IDs aus `build.gradle.kts`). bundletool fehlt auf den Runnern, deshalb feste Version mit SHA-256. |
| App-Bundle als Artifact im öffentlichen Repo | Hingenommen: ohne Secrets, nichts, was nicht aus dem öffentlichen Code folgt (auch das eingebettete R8-Mapping nicht), ohne Dart-Symbole, verfällt nach einem Tag. |
| JSON-Schlüssel eines Service-Accounts | Bewusst, aus anderen Projekten vertraut. Verworfen: Workload Identity Federation, obwohl fastlane 2.242 sie kann. Preis: ein Schlüssel ohne Ablaufdatum, begrenzt durch die enge Berechtigung. |
| Schlüssel des Service-Accounts im Environment `development` | Nur der Upload bei Push auf `main` braucht ihn, das Environment lässt nur `main` zu, Pull Requests kommen nicht heran. `ANDROID_*` und Apple-Secrets brauchen Pull Requests zum Signieren. |
| Secrets mit Dateiinhalt als Base64 | Einheitlich, niemand muss nachsehen, welches kodiert ist. Auch der JSON-Schlüssel, obwohl `json_key_data` ihn roh annähme. |
| „Release apps to testing tracks“ nur für „Murmli Dev“ | fastlane empfiehlt Admin. So kann ein geleakter Schlüssel nur in die Test-Tracks der Dev-App veröffentlichen. |
| Gruppe `google-play-upload` am Android-Job von `deliver-app.yml`, ohne Cancel | Ein beginnender Upload löscht die offene Bearbeitung (Edit) desselben Service-Accounts, gleichzeitige Uploads (etwa nach zwei schnellen Merges) brechen sich ab („This Edit has been deleted“). Ohne Cancel, damit kein laufender Upload abbricht, einen wartenden ersetzt GitHub durch den neueren. |
| Upload auf ubuntu | supply hinter `upload_to_play_store` ist reines Ruby und spricht die Play Developer API direkt an, anders als altool auf iOS. |
