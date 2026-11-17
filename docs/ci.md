# CI

`ci.yml` läuft bei Push auf `main` und bei Pull Requests und ruft `lint.yml`, `test.yml`, `build-app.yml` und `vulnerability-scan.yml` auf, auf `main` danach `deploy.yml` (Ablauf und Wiederholungen von `aspire deploy` in [azure.md](azure.md)).

Lokale Checks (Befehle in [AGENTS.md](../AGENTS.md#ci)):

- `dotnet test` braucht Docker, Node in der Version aus `src/auth/.nvmrc` und beim ersten Build Netzwerk für `dnx aspire.cli` (sonst ASPIRE009). Node, weil die AppHost-Tests `auth` starten: Aspires Installer führt im Run-Mode vor jedem Start `npm install` in `src/auth` aus.
- `dotnet restore` meldet neue Advisories nur mit `--force`.
- `npm run check` in `src/auth` ist `biome ci && tsc --noEmit`, Lint und Formatierung macht Biome. `npm run format` (`biome check --write`) wendet Formatierung und sichere Fixes an.

## App

- `build-app.yml` baut den Flavor aus seinem Pflicht-Input `flavor`, `ci.yml` übergibt `development` (Flavors in [app.md](app.md)): Android-App-Bundle auf ubuntu, iOS ohne Codesigning auf macOS.
- `test.yml` ruft `flutter test` ohne `--flavor` auf, der Test des Standard-Flavors verlässt sich darauf.

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
