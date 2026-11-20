# Apple

Signieren der iOS-App und Auslieferung über TestFlight. Flavors und Bundle-IDs in [app.md](app.md#flavors), Sentry-Upload in [ci.md](ci.md#app).

## Einrichtung bei Apple

- Team Murmli im Apple Developer Program.
- App IDs `de.murmli.app` und `de.murmli.app.dev`, ohne Capabilities.
- Ein Zertifikat „Apple Distribution“, gültig etwa ein Jahr ([erneuern](#zertifikat-erneuern)).
- Team-Schlüssel der App Store Connect API mit der Rolle Admin.
- App-Store-Profil „Murmli Dev App Store“ für `de.murmli.app.dev` (legt `sigh` an, wenn es fehlt).
- App Store Connect: App „Murmli Dev“ mit interner TestFlight-Gruppe mit automatischer Verteilung, jeder verarbeitete Build geht direkt an die Tester. TestFlight-Builds laufen nach 90 Tagen ab.
- `production`: [`production` bauen](#production-bauen).

## Build in der CI

- `prepare_signing` stellt nur auf dem Runner die Configuration `Release-<flavor>` auf manuelles Signieren mit Team und Profil um.
- `flutter build ipa` (Stand 3.50) erzeugt bei `CODE_SIGN_STYLE = Manual` die Export-Options selbst und findet das Profil per Namen in `~/Library/Developer/Xcode/UserData/Provisioning Profiles`, wo fastlane es für Xcode 16 und neuer ablegt.
- `flutter build ipa` endet auch erfolgreich, wenn nur der Export scheitert, deshalb prüft der Step, ob eine IPA entstand.
- Mit `deliver`: Symbole nach Sentry ([ci.md](ci.md#app)), IPA als Artifact `ios-ipa` für einen Tag.

## Upload nach TestFlight

`deliver-app.yml` lädt `ios-ipa` per `upload_to_testflight` hoch, ohne auf die Verarbeitung zu warten. Zeitpunkt und macOS: [Entscheidungen](#entscheidungen).

## Secrets

Repository-Secrets ([Entscheidungen](#entscheidungen)): `APPLE_CERTIFICATE_DATA` (.p12, Base64), `APPLE_CERTIFICATE_PASSPHRASE`, `APPLE_APP_STORE_CONNECT_KEY` (.p8, Base64), `APPLE_APP_STORE_CONNECT_KEY_ID`, `APPLE_APP_STORE_CONNECT_ISSUER_ID`, `APPLE_TEAM_ID`.

## Build-Nummer

- Build-Nummer ist `github.run_number` (`CFBundleVersion`, auf Android `versionCode`), Pull Requests hinterlassen Lücken.
- TestFlight lehnt eine schon hochgeladene Build-Nummer ab, „Re-run all jobs“ nach einem erfolgreichen Upload scheitert deshalb.
- „Re-run failed jobs“ wiederholt Upload oder Deploy einzeln, den Upload mit der App des Laufs, solange das Artifact existiert (ein Tag). Danach baut „Re-run all jobs“ neu mit derselben Build-Nummer, und der Upload der anderen Plattform scheitert, falls er schon gelungen war.

## Abläufe

### Zertifikat erneuern

1. Neues Zertifikat „Apple Distribution“ anlegen, solange das alte gilt (höchstens drei pro Team).
2. Als .p12 exportieren, `APPLE_CERTIFICATE_DATA` (Base64) und `APPLE_CERTIFICATE_PASSPHRASE` ersetzen.
3. Profil „Murmli Dev App Store“ im Developer-Portal löschen, sonst scheitert `sigh` am vergebenen Namen (`fail_on_name_taken`).
4. Erst dann das alte Zertifikat widerrufen: Hochgeladene, nicht eingereichte Builds eines widerrufenen Zertifikats können zu „Invalid Binary“ werden.

### Neue Vereinbarungen

Nach neuen oder geänderten Vereinbarungen scheitern Uploads und API-Aufrufe mit 403 „A required agreement is missing or has expired“, bis der Account Holder sie in App Store Connect unter „Business“ annimmt.

### Neue Capability

Etwa „Sign in with Apple“ ([#53](https://github.com/Murmli/MurmliV2/issues/53)). In dieser Reihenfolge, sonst scheitert das Signieren im Pull Request am alten Profil:

1. Capability an beiden App IDs aktivieren, Profil „Murmli Dev App Store“ löschen (`sigh` legt es neu an).
2. Im Pull Request die Entitlements-Datei des Targets `Runner` ergänzen und per `CODE_SIGN_ENTITLEMENTS` in allen Configurations eintragen.

### `production` bauen

Noch nicht gebaut, die CI baut nur `development` (Abschnitt entfällt, sobald sie `production` baut). Es fehlen ein App-Eintrag „Murmli“ für `de.murmli.app` in App Store Connect (ein Mensch) und ein Eintrag für `production` in der Signing-Map von `prepare_signing`.

### fastlane aktualisieren

Im Repo-Root:

```bash
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD/src/app:/work" -w /work "ruby:$(cat src/app/.ruby-version)-slim" bundle lock --add-platform arm64-darwin --update fastlane
```

Nach einer Änderung am `Gemfile` ohne `--update fastlane`.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Signieren per fastlane (`prepare_signing`), Bauen per `flutter build ipa` | Verworfen: `flutter build ios --config-only` plus `build_app`/gym (BoundfoxStudios/Flugwacht). Flavor, Build-Nummer und `--split-debug-info` müssten über `Generated.xcconfig` hinein, das Archiv läge außerhalb der Suchpfade von `sentry_dart_plugin` (Stand 3.5.0). |
| Manuelles Signieren | Automatisches legt auf jedem neuen Runner ein Entwicklungszertifikat an, bis das Kontingent des Teams erschöpft ist (in Flugwacht passiert). |
| Kein `DEVELOPMENT_TEAM` im Xcode-Projekt | Die CI setzt das Team pro Lauf, der Simulator braucht keins. |
| Kein fastlane match | Bräuchte Repo oder Bucket für Zertifikate und Profile plus `MATCH_PASSWORD`. |
| Signieren auch in Pull Requests | Abgelaufenes Zertifikat, Profil ohne neue Capability oder fehlende Vereinbarung fallen vor dem Merge auf. |
| Apple-Secrets im Repository, nicht im Environment `development` | Pull Requests signieren, das Environment lässt nur `main` zu. Pull Requests aus Forks scheitern ohne Secrets, hingenommen: Nur Maintainer öffnen welche. |
| Upload in `deliver-app.yml` nach allen Checks, parallel zu `deploy-backend` (Android ebenso) | In `build-app` (parallel zu `lint`, `test`, `vulnerability-scan`) kämen auch Builds mit roten Checks in den Test. Upload und Deploy blockieren sich nicht gegenseitig. |
| Upload auf macOS | `upload_to_testflight` nutzt altool, das es nur auf macOS gibt. Unter Linux (Stand fastlane 2.242.0) bräuchte es Apples Transporter für Linux, den die Runner nicht haben, und eine `AppStoreInfo.plist`, die `flutter build ipa` nicht erzeugt. |
| IPA als Artifact im öffentlichen Repo | Hingenommen: nur für den App Store signiert (Profil ohne Geräte), ohne Secrets und Dart-Symbole, der Rest (auch die Team-ID im eingebetteten Profil) ist ohnehin öffentlich, verfällt nach einem Tag. |
| `ITSAppUsesNonExemptEncryption` auf `false` (`Info.plist`) | Nur HTTPS, das ist von der Exportkontrolle ausgenommen. Ohne den Eintrag wartet jeder Build auf „Missing Compliance“. |
| API-Schlüssel mit der Rolle Admin | `sigh` legt Profile an, dokumentiert ist das nur für Admin, nicht für App Manager. |
