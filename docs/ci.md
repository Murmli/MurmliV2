# CI

`ci.yml` läuft bei Push auf `main` und bei Pull Requests und ruft `lint.yml`, `test.yml`, `build-app.yml` und `vulnerability-scan.yml` auf, auf `main` danach `deploy.yml` (Ablauf und Wiederholungen von `aspire deploy` in [azure.md](azure.md)) und parallel dazu `deliver-app.yml` ([apple.md](apple.md#upload-nach-testflight)).

Lokale Checks (Befehle in [AGENTS.md](../AGENTS.md#ci)):

- `dotnet test` braucht Docker, Node in der Version aus `src/auth/.nvmrc` und beim ersten Build Netzwerk für `dnx aspire.cli` (sonst ASPIRE009). Node, weil die AppHost-Tests `auth` starten: Aspires Installer führt im Run-Mode vor jedem Start `npm install` in `src/auth` aus.
- `dotnet restore` meldet neue Advisories nur mit `--force`.
- `npm run check` in `src/auth` ist `biome ci && tsc --noEmit`, Lint und Formatierung macht Biome. `npm run format` (`biome check --write`) wendet Formatierung und sichere Fixes an.

## App

- `build-app.yml` baut den Flavor aus seinem Pflicht-Input `flavor`, `ci.yml` übergibt `development` (Flavors in [app.md](app.md)): in jedem Lauf (Pull Requests und Push) ein signiertes Android-App-Bundle auf ubuntu (Signieren und Google Play in [google-play.md](google-play.md)) und eine signierte iOS-IPA auf macOS (Signieren und TestFlight in [apple.md](apple.md)).
- `test.yml` ruft `flutter test` ohne `--flavor` auf, der Test des Standard-Flavors verlässt sich darauf.
- `build-app.yml` baut mit `--split-debug-info=build/debug-info`, ohne `--obfuscate`. Mit dem Input `deliver` (aus `ci.yml` nur bei Push auf `main`) laden beide Jobs die Symbole nach Sentry (Projekt `app`, siehe [app.md](app.md#absturzberichte-sentry)) und legen zusätzlich die App als Artifact ab, der iOS-Job die IPA ([apple.md](apple.md#build-in-der-ci)), der Android-Job das App-Bundle ([google-play.md](google-play.md#build-in-der-ci)). Android lädt das R8-Mapping schon im Build über das Sentry Android Gradle Plugin (Gradle-Property `sentryUploadProguardMapping`, gesetzt per `ORG_GRADLE_PROJECT_sentryUploadProguardMapping`). Danach lädt `dart run sentry_dart_plugin` in beiden Jobs die Dart-Symbole aus `build/debug-info` und die nativen Debug-Dateien, auf Android aus `build/app`, auf iOS die dSYMs aus dem Archiv in `build/ios/archive`. Die Konfiguration steht im Abschnitt `sentry:` der `pubspec.yaml`, ohne Commit-Zuordnung (`commits: false`, die automatische Zuordnung bräuchte eine Repository-Integration in Sentry). Beide Apps melden die ID und Build-Nummer, mit denen sie gebaut wurden, deshalb übergibt die CI `SENTRY_RELEASE` (`<ID>@<Version>+<Build>`) und `SENTRY_DIST` (`<Build>`): auf iOS aus der `Info.plist` der archivierten App (`CFBundleIdentifier`, `CFBundleShortVersionString`, `CFBundleVersion`), auf Android per bundletool aus dem Manifest des App-Bundles (`package`, `versionName`, `versionCode`, siehe [google-play.md](google-play.md#build-in-der-ci)). Der Abschnitt `sentry:` nennt weiterhin den Release `de.murmli.app@<Version>+<Build>` mit Version und Build-Nummer der `pubspec.yaml`, der nur ohne `SENTRY_RELEASE` gilt.
- Das Organisations-Token `SENTRY_AUTH_TOKEN` (enthält Organisation und Region) liegt nur als Secret im Environment `development`. Environment-Secrets erreichen nur Jobs, die das Environment nennen: Die Jobs von `build-app.yml` nennen es nur mit `deliver`, mit `deployment: false`, also ohne Deployment-Eintrag. `ci.yml` reicht das Secret trotzdem namentlich durch, sonst käme es in `build-app.yml` nicht an. Fehlt es auf `main`, bricht der Job vor dem Build ab: Ein fehlendes Secret käme als leerer String an, den `sentry_dart_plugin` erst beim Upload ablehnt.
- Lokale Builds und Pull Requests laden nie hoch: Ohne die Gradle-Property bleibt das Gradle-Plugin im Dry-Run und bettet nur die UUID des Mappings in die App ein, `sentry_dart_plugin` läuft nur in CI. Das Gradle-Plugin sendet keine eigene Telemetrie (`telemetry`), instrumentiert keinen App-Code (`tracingInstrumentation`, damit auch keine Performance-Spans und keine Logcat-Breadcrumbs) und installiert kein Android-SDK (`autoInstallation`, das bringt `sentry_flutter` mit). Nur die standardmäßig aktive Laufzeit-Optimierung (`runtimeOptimizations`) ändert Bytecode: Sie ersetzt in zwei Klassen des Sentry-SDK Prüfungen zur Laufzeit (verfügbare Klassen, Manifest-Metadaten) durch Werte aus dem Build.
- `vulnerability-scan.yml` scannt `src/app` rekursiv, also auch `src/app/Gemfile.lock` mit fastlane und seinen Abhängigkeiten, die in der CI mit den Apple-Secrets laufen. Funde ohne Fix kommen wie die der App in `src/app/osv-scanner.toml`.

## Auth

- Die Composite-Action `.github/actions/setup-auth` installiert Node aus `src/auth/.nvmrc` mit npm-Cache und führt `npm ci` aus.
- `lint.yml` prüft `auth` im Job Auth (`npm run check`), `test.yml` ruft `setup-auth` im Job Backend vor `dotnet test` auf.
- `vulnerability-scan.yml` scannt `src/auth` im Job Auth mit osv-scanner.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `main` geschützt, Ready for review und Merge nur durch einen Menschen | Jeder Merge auf `main` deployt Dev. |
| Concurrency nur in `ci.yml` | In Called Workflows blockiert oder cancelt sie den Caller. |
| Gruppe `deploy-dev` am Job, ohne Cancel | Ein Abbruch bräche laufende ARM-Deployments ab. |
| Keine Gruppe am aufrufenden `deploy`-Job | Sonst wartet der Deploy auf eine Sperre, die sein eigener Caller hält. |
| Backend-Formatierung nur mit `dotnet format` (ohne `.editorconfig`) | Geprüft und vorerst verworfen (2026-11-14): CSharpier bricht Argumente nur nach Zeilenbreite um und widerspricht dem vollen `dotnet format`; ReSharper `jb cleanupcode` erzwingt ein Argument pro Zeile, verlängert den Code aber stark, ist 190 MB groß, proprietär und hat keinen Prüfmodus. `dotnet format` selbst kann Argumentlisten weder umbrechen noch prüfen. |
| osv-scanner-action auf exakter Version | Die Action hat keinen gleitenden Major-Tag. |
| `tsc --noEmit` mit dem Stub `src/auth/src/types/bun-sqlite.d.ts` statt `skipLibCheck` | Better Auths Typen importieren `bun:sqlite`, ohne Stub scheitert `tsc`. `skipLibCheck` machte die Option `database` still zu `any`. |
| Symbole nur aus der CI bei Push auf `main` | Symbole passen nur zu dem Build, aus dem sie stammen. Das Token bleibt aus Pull Requests und lokalen Umgebungen heraus. |
| Kein `--obfuscate` | Für lesbare Stacktraces reicht `--split-debug-info`. Laut Flutter lieferten `runtimeType` und `Enum.toString()` verschleierte Namen, und Issue-Titel bräuchten zusätzlich die Obfuscation-Map. |
| R8-Mapping über das Sentry Android Gradle Plugin | Das Android-SDK ordnet das Mapping über eine UUID in den Assets der App zu, die das Plugin vor R8 erzeugt und beim Upload mitgibt. `sentry_dart_plugin` lädt keine Mappings hoch. |
| Upload-Fehler lassen den Job scheitern | Fehlende Symbole sollen auffallen, auch um den Preis eines blockierten Deploys. |
| Environment mit `deployment: false` | Build-Jobs deployen nichts, Deployments nach `development` stammen nur aus `deploy.yml`. |
