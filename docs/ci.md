# CI

Auslieferung von `main`: `deploy-backend.yml` ([azure.md](azure.md)), parallel `deliver-app.yml` nach TestFlight ([apple.md](apple.md#upload-nach-testflight)) und Google Play ([google-play.md](google-play.md#upload-nach-google-play)).

Lokale Checks (Befehle in [AGENTS.md](../AGENTS.md#ci)):

- `dotnet test` braucht Docker, Node aus `src/auth/.nvmrc` (die AppHost-Tests starten `auth`, Aspire führt davor `npm install` in `src/auth` aus) und beim ersten Build Netzwerk für `dnx aspire.cli` (sonst ASPIRE009).
- `dotnet restore` meldet neue Advisories nur mit `--force`.

## App

- Das R8-Mapping lädt das Sentry Android Gradle Plugin schon im Build, nur mit der Gradle-Property `sentryUploadProguardMapping` (gesetzt per `ORG_GRADLE_PROJECT_sentryUploadProguardMapping`). Ohne sie bleibt es im Dry-Run und bettet nur die UUID des Mappings in die App ein. Dart-Symbole und native Debug-Dateien lädt danach `dart run sentry_dart_plugin`.
- Bytecode ändert das Gradle-Plugin nur über die standardmäßig aktive `runtimeOptimizations`: Sie ersetzt in zwei Klassen des Sentry-SDK Prüfungen zur Laufzeit (verfügbare Klassen, Manifest-Metadaten) durch Werte aus dem Build.
- Release und Dist: Die Apps melden den Release `<ID>@<Version>+<Build>` ihres Builds (`development` mit `de.murmli.app.dev`). Der Abschnitt `sentry:` der `pubspec.yaml` nennt nur `de.murmli.app` mit Version und Build-Nummer der `pubspec.yaml` und gilt nur ohne `SENTRY_RELEASE`. Die CI übergibt deshalb `SENTRY_RELEASE` und `SENTRY_DIST` (`<Build>`) der gebauten App: iOS aus der `Info.plist` des Archivs, Android per bundletool aus dem Manifest des App-Bundles ([google-play.md](google-play.md#build-in-der-ci)). Ohne Commit-Zuordnung (`commits: false`), sie bräuchte eine Repository-Integration in Sentry.
- `SENTRY_AUTH_TOKEN` ist ein Organisations-Token (enthält Organisation und Region) und liegt nur im Environment `development`. Environment-Secrets erreichen nur Jobs, die das Environment nennen, `ci.yml` muss das Secret trotzdem namentlich an `build-app.yml` durchreichen. Ein fehlendes Secret käme als leerer String an, den `sentry_dart_plugin` erst beim Upload ablehnt, deshalb prüft der Job es vor dem Build.
- `vulnerability-scan.yml` scannt `src/app` rekursiv und damit auch `Gemfile.lock`: fastlane läuft in der CI mit den Apple-Secrets und dem Schlüssel für Google Play ([google-play.md](google-play.md#secrets)).

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `main` geschützt, Ready for review und Merge nur durch einen Menschen | Jeder Merge auf `main` deployt Dev. |
| Concurrency nur in `ci.yml` | In Called Workflows blockiert oder cancelt sie den Caller. |
| Gruppe `deploy-dev` am Job, ohne Cancel | Ein Abbruch bräche laufende ARM-Deployments ab. |
| Keine Gruppe am aufrufenden `deploy-backend`-Job | Sonst wartet der Deploy auf eine Sperre, die sein eigener Caller hält. |
| Gruppe `google-play-upload` am Android-Job von `deliver-app.yml`, ohne Cancel | Gleichzeitige Uploads nach Google Play scheitern gegenseitig ([google-play.md](google-play.md#entscheidungen)). |
| Backend-Formatierung nur mit `dotnet format` (ohne `.editorconfig`) | Verworfen (2026-11-14): CSharpier bricht Argumente nur nach Zeilenbreite um und widerspricht dem vollen `dotnet format`. ReSharper `jb cleanupcode` erzwingt ein Argument pro Zeile, verlängert den Code aber stark, ist 190 MB groß, proprietär und ohne Prüfmodus. `dotnet format` kann Argumentlisten weder umbrechen noch prüfen. |
| osv-scanner-action auf exakter Version | Die Action hat keinen gleitenden Major-Tag. |
| `tsc --noEmit` mit dem Stub `src/auth/src/types/bun-sqlite.d.ts` statt `skipLibCheck` | Better Auths Typen importieren `bun:sqlite`, ohne Stub scheitert `tsc`. `skipLibCheck` machte die Option `database` still zu `any`. |
| Symbole nur aus der CI bei Push auf `main` | Symbole passen nur zu dem Build, aus dem sie stammen. Das Token bleibt aus Pull Requests und lokalen Umgebungen heraus. |
| Kein `--obfuscate` | `--split-debug-info` reicht für lesbare Stacktraces. Laut Flutter lieferten mit Obfuscation `runtimeType` und `Enum.toString()` verschleierte Namen, und Issue-Titel bräuchten die Obfuscation-Map. |
| R8-Mapping über das Sentry Android Gradle Plugin | Das Android-SDK ordnet das Mapping über eine UUID in den Assets der App zu, die das Plugin vor R8 erzeugt. `sentry_dart_plugin` lädt keine Mappings hoch. |
| Upload-Fehler lassen den Job scheitern | Fehlende Symbole sollen auffallen, auch um den Preis eines blockierten Deploys. |
| Environment mit `deployment: false` | Build- und Upload-Jobs deployen nichts, Deployments nach `development` stammen nur aus `deploy-backend.yml`. |
| Backend-Job mit `timeout-minutes: 20` | Die Tests des Abgleichs mit gleichzeitigen Anfragen können bei einem Fehler hängen; ohne Timeout liefe der Job bis zum Standard von 6 Stunden. |
