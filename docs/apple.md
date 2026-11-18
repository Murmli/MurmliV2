# Apple

Signieren der iOS-App und Auslieferung über TestFlight. Flavors, Bundle-IDs und Namen in [app.md](app.md#flavors), Sentry-Upload und übrige App-CI in [ci.md](ci.md#app).

## Einrichtung bei Apple

Bis auf das Profil, das die CI anlegt, legt alles in diesem Abschnitt nur ein Mensch an, ändert oder löscht es.

- Eigenes Team Murmli im Apple Developer Program.
- App IDs für beide Bundle-IDs aus [app.md](app.md#flavors), ohne Capabilities.
- Ein Zertifikat „Apple Distribution“ (gültig etwa ein Jahr), Erneuerung siehe [Zertifikat erneuern](#zertifikat-erneuern).
- Ein Team-Schlüssel der App Store Connect API mit der Rolle Admin.
- Das App-Store-Provisioning-Profil „Murmli Dev App Store“ für `de.murmli.app.dev` (legt die CI an, siehe [Build in der CI](#build-in-der-ci)).
- App Store Connect: App-Eintrag „Murmli Dev“ für `de.murmli.app.dev` und eine interne TestFlight-Gruppe mit automatischer Verteilung, jeder verarbeitete Build geht ohne weiteren Schritt an ihre Tester.
- Tester nehmen die Einladung in der TestFlight-App an. TestFlight-Builds laufen nach 90 Tagen ab.
- `production` hat noch keinen App-Eintrag und keinen Eintrag im Fastfile, siehe [`production` bauen](#production-bauen).

## Build in der CI

Der iOS-Job von `build-app.yml` läuft auf macOS und signiert in jedem Lauf, bei Pull Requests wie bei Push auf `main`. Ruby kommt per `ruby/setup-ruby` in der Version aus `src/app/.ruby-version`, fastlane über `bundle exec` aus `src/app/Gemfile` und `src/app/Gemfile.lock`, die Lanes stehen in `src/app/fastlane/Fastfile`.

1. `bundle exec fastlane ios prepare_signing flavor:<flavor>`: `setup_ci` legt einen temporären Schlüsselbund an, in den das Zertifikat aus `APPLE_CERTIFICATE_DATA` kommt. `sigh` holt über den API-Schlüssel das Profil des Flavors aus der Signing-Map der Lane (derzeit nur `development` mit „Murmli Dev App Store“) oder legt es an. `update_code_signing_settings` stellt die Configuration `Release-<flavor>` des Targets `Runner` auf manuelles Signieren mit Team, Identität „Apple Distribution“ und diesem Profil um. Die Änderung am Xcode-Projekt bleibt auf dem Runner.
2. `flutter build ipa --release --flavor <flavor> --split-debug-info=build/debug-info --build-number=<github.run_number>`: Flutter (Stand 3.50) erkennt `CODE_SIGN_STYLE = Manual` und erzeugt die Export-Options selbst, mit Team, manuellem Signieren und der UUID des Profils, das es per Namen in `~/Library/Developer/Xcode/UserData/Provisioning Profiles` findet. Dort legt fastlane Profile für Xcode 16 und neuer ab. Das Archiv landet in `src/app/build/ios/archive/Runner.xcarchive`. Weil `flutter build ipa` auch erfolgreich endet, wenn nur der Export scheitert, prüft der Step danach, ob in `src/app/build/ios/ipa` eine IPA liegt.
3. Nur mit dem Input `deliver`: Upload der Symbole nach Sentry mit Release und Dist aus der `Info.plist` der archivierten App ([ci.md](ci.md#app)).
4. Nur mit `deliver`: Der Job legt die IPA als Artifact `ios-ipa` für einen Tag ab ([Upload nach TestFlight](#upload-nach-testflight)).
5. Immer, auch nach Fehlern und Abbrüchen, als letzter Step: „Remove signing secrets“ löscht `distribution.p12` und `AuthKey.p8` aus `$RUNNER_TEMP` und den temporären Schlüsselbund mit dem privaten Schlüssel.

- fastlane läuft nur in der CI, nie lokal oder durch Agents.
- `src/app/ios/Runner/Info.plist` setzt `ITSAppUsesNonExemptEncryption` auf `false`.

## Upload nach TestFlight

`deliver-app.yml` läuft nur bei Push auf `main`, nach `lint`, `test`, `build-app` und `vulnerability-scan` und parallel zu `deploy`. Sein Job iOS läuft auf macOS, Ruby und fastlane kommen wie beim [Build](#build-in-der-ci) aus `src/app`.

1. Der Job lädt das Artifact `ios-ipa` nach `src/app/build/ios/ipa` herunter.
2. `bundle exec fastlane ios upload` lädt die IPA mit dem API-Schlüssel aus `$RUNNER_TEMP/AuthKey.p8` per `upload_to_testflight` hoch, ohne auf die Verarbeitung zu warten.
3. Immer, auch nach Fehlern und Abbrüchen, als letzter Step: „Remove signing secrets“ löscht `AuthKey.p8` aus `$RUNNER_TEMP` und die Kopien des API-Schlüssels, die fastlane nur beim Upload anlegt (`deliver-*` unter `$TMPDIR`, `~/.appstoreconnect/private_keys`).

Upload und Deploy nach Dev blockieren sich nicht gegenseitig.

## Secrets

- Repository-Secrets: `APPLE_CERTIFICATE_DATA` (.p12 des Zertifikats als Base64), `APPLE_CERTIFICATE_PASSPHRASE`, `APPLE_APP_STORE_CONNECT_KEY` (.p8 des API-Schlüssels als Base64), `APPLE_APP_STORE_CONNECT_KEY_ID`, `APPLE_APP_STORE_CONNECT_ISSUER_ID` und `APPLE_TEAM_ID`.
- Sie liegen im Repository, nicht im Environment `development` (Grund unter [Entscheidungen](#entscheidungen)). `SENTRY_AUTH_TOKEN` bleibt im Environment ([ci.md](ci.md#app)).
- `ci.yml` reicht sie namentlich an `build-app.yml`, an `deliver-app.yml` nur `APPLE_APP_STORE_CONNECT_KEY`, `APPLE_APP_STORE_CONNECT_KEY_ID` und `APPLE_APP_STORE_CONNECT_ISSUER_ID`, kein Zertifikat. Beide prüfen in einem eigenen Step vor dem Build bzw. Upload, dass keines der übergebenen Secrets leer ist.

## Build-Nummer

- iOS nutzt `github.run_number` als Build-Nummer (`CFBundleVersion`): Sie steigt mit jedem Lauf von `ci.yml`, Läufe von Pull Requests hinterlassen Lücken.
- TestFlight lehnt eine schon hochgeladene Build-Nummer ab, „Re-run all jobs“ nach einem erfolgreichen Upload scheitert deshalb beim Upload.
- `deploy` und `deliver-app` lassen sich unabhängig voneinander wiederholen. Nach einem gescheiterten Deploy wiederholt „Re-run failed jobs“ nur den Deploy. Nach einem gescheiterten Upload wiederholt es den Upload mit der IPA des Laufs, solange das Artifact existiert (ein Tag). Ist es abgelaufen, baut „Re-run all jobs“ neu, mit derselben Build-Nummer, die der gescheiterte Upload nicht verbraucht hat. Re-runs auf `main` startet ohnehin nur ein Mensch.
- Android behält seine bisherige Build-Nummer bis [#102](https://github.com/Murmli/MurmliV2/issues/102).

## Abläufe

### Zertifikat erneuern

1. Ein neues Zertifikat „Apple Distribution“ anlegen, solange das alte noch gilt (höchstens drei pro Team).
2. Es als .p12 exportieren und `APPLE_CERTIFICATE_DATA` (Base64) und `APPLE_CERTIFICATE_PASSPHRASE` ersetzen.
3. Das Profil „Murmli Dev App Store“ im Developer-Portal löschen. Sonst scheitert `sigh` am vergebenen Namen (`fail_on_name_taken`), statt ein Profil für das neue Zertifikat anzulegen.
4. Das alte Zertifikat erst danach widerrufen: Hochgeladene, noch nicht eingereichte Builds eines widerrufenen Zertifikats können zu „Invalid Binary“ werden.

### Neue Vereinbarungen

Nach neuen oder geänderten Vereinbarungen von Apple scheitern Uploads und Aufrufe der API mit 403 „A required agreement is missing or has expired“, bis der Account Holder sie in App Store Connect unter „Business“ annimmt.

### Neue Capability

Etwa „Sign in with Apple“ für [#53](https://github.com/Murmli/MurmliV2/issues/53): Zuerst die Capability an beiden App IDs aktivieren und das Profil „Murmli Dev App Store“ im Developer-Portal löschen, damit `sigh` es mit der Capability neu anlegt. Danach im Pull Request die Entitlements-Datei des Targets `Runner` ergänzen und per `CODE_SIGN_ENTITLEMENTS` in allen Configurations eintragen. In umgekehrter Reihenfolge scheitert das Signieren im Pull Request, weil das alte Profil das Entitlement nicht enthält.

### `production` bauen

Noch nicht gebaut, die CI baut nur `development`. Dieser Abschnitt entfällt, sobald sie `production` baut. Dafür braucht es einen App-Eintrag „Murmli“ für `de.murmli.app` in App Store Connect (ein Mensch) und einen Eintrag für `production` in der Signing-Map von `prepare_signing` im Fastfile.

### fastlane aktualisieren

`src/app/Gemfile` erlaubt fastlane `~> 2.242`. `src/app/Gemfile.lock` nie von Hand ändern, sondern mit Docker im Repo-Root neu erzeugen, mit der Ruby-Version aus `src/app/.ruby-version`:

```bash
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD/src/app:/work" -w /work "ruby:$(cat src/app/.ruby-version)-slim" bundle lock --add-platform arm64-darwin --update fastlane
```

Nach einer Änderung am `Gemfile` derselbe Befehl ohne `--update fastlane`.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Signieren per fastlane (`prepare_signing`), Bauen per `flutter build ipa` | Verworfen: der Weg aus BoundfoxStudios/Flugwacht (`flutter build ios --config-only`, dann `build_app`/gym). Die App wird nur per `flutter build` mit `--flavor` gebaut, das Archiv von gym läge außerhalb der Suchpfade von `sentry_dart_plugin` (Stand 3.5.0), und Flavor, Build-Nummer und `--split-debug-info` müssten über `Generated.xcconfig` hineingeschmuggelt werden. |
| Manuelles Signieren | Verworfen: automatisches Signieren, es legt auf jedem neuen Runner ein neues Entwicklungszertifikat an, bis das Kontingent des Teams erschöpft ist (in Flugwacht passiert). |
| Keine Team-ID im Xcode-Projekt | Die CI setzt das Team pro Lauf aus `APPLE_TEAM_ID`, im Simulator braucht die App keins. |
| Kein fastlane match | Bräuchte ein Repo oder einen Bucket für Zertifikate und Profile und zusätzlich `MATCH_PASSWORD`. |
| Signieren auch in Pull Requests | Fehler beim Signieren (abgelaufenes Zertifikat, Profil ohne neue Capability, fehlende Vereinbarung) fallen im Pull Request auf statt erst nach dem Merge auf `main`. |
| Apple-Secrets im Repository statt im Environment `development` | Pull Requests signieren auch, das Environment lässt nur `main` zu. Pull Requests aus Forks bekommen keine Secrets und scheitern an deren Prüfung, das ist hingenommen: Pull Requests öffnen nur Maintainer. |
| Upload nach TestFlight in `deliver-app.yml` nach allen Checks, parallel zu `deploy`, ohne Gruppe `deploy-dev` | `build-app` läuft parallel zu `lint`, `test` und `vulnerability-scan`, ein Upload dort brächte einen Build nach TestFlight, obwohl Tests oder Scan desselben Laufs rot sind. Ein gescheiterter Upload soll den Deploy nach Dev nicht blockieren und umgekehrt. Android soll demselben Muster folgen (Upload nach Google Play in `deliver-app.yml`: entschieden, noch nicht gebaut; diese Klammer entfällt, sobald `deliver-app.yml` nach Google Play lädt). Keine Gruppe `deploy-dev`, weil der Upload Azure Dev nicht ändert. |
| Upload auf macOS | `upload_to_testflight` lädt per altool hoch, das es nur auf macOS gibt. fastlanes Upload unter Linux (Stand 2.242.0) bräuchte Apples Transporter für Linux auf dem Runner, den die GitHub-Runner nicht mitbringen, und eine `AppStoreInfo.plist` neben der IPA, die `flutter build ipa` nicht erzeugt. |
| IPA als Artifact im öffentlichen Repo | Angemeldete GitHub-Nutzer können es herunterladen, das ist hingenommen: Die IPA ist für den App Store signiert und lässt sich nicht auf Geräten installieren (das Profil enthält keine Geräte), sie enthält keine Secrets, und ihr Inhalt (Dart-AOT-Snapshot ohne Symbole, Assets, `Info.plist`, eingebettetes Profil mit Team-ID und App ID) ist ohnehin öffentlich. Das Artifact verfällt nach einem Tag. |
| `ITSAppUsesNonExemptEncryption` auf `false` | Die App verschlüsselt nur per HTTPS, das von der Exportkontrolle ausgenommen ist. Ohne den Eintrag wartet jeder Build auf „Missing Compliance“. |
| API-Schlüssel mit der Rolle Admin | `sigh` legt Profile an, das ist für Admin dokumentiert, für App Manager nicht. |
