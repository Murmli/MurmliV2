# VS Code

`.vscode` enthält Launch-Konfigurationen, Tasks, Workspace-Settings und Extension-Empfehlungen. VS Code wird immer im Repo-Root geöffnet, alle Pfade darin sind relativ dazu.

## Launch-Konfigurationen

- `Backend (Aspire)` startet den AppHost über die Aspire-Extension, die dafür die Aspire CLI auf dem PATH braucht. Voraussetzungen wie bei `aspire run`: Docker, Node aus `src/auth/.nvmrc` und die User Secrets des AppHost ([AGENTS.md](../AGENTS.md#aspire-und-azure)).
- `App (local)` und `App (development)` starten die App mit dem jeweiligen Flavor (`--flavor`), `local` braucht das laufende Backend ([app.md](app.md)).
- `Backend + App (local)` startet beides, `stopAll` beendet beim Stoppen einer Konfiguration auch die andere.

## Tasks

`tasks.json` enthält die lokalen Checks aus [AGENTS.md](../AGENTS.md#ci) als Tasks (`App: …`, `Backend: …`, `Auth: …`), ohne `pub get`, `npm ci` und `dotnet restore`. `App: test` und `Backend: test` liegen in der Gruppe `test` („Tasks: Run Test Task“).

## Settings

- `dart.flutterSdkPath` zeigt auf `src/app/.fvm/flutter_sdk`. Den Link legt `fvm install` in `src/app` an, er ist nicht im Repo. So nutzt die Dart-Extension die Flutter-Version aus `src/app/.fvmrc`, obwohl der Workspace der Repo-Root ist.
- Format-on-Save für Dart (`dart format`) und C# (Formatter der C#-Extension), damit `dart format --set-exit-if-changed` und `dotnet format --verify-no-changes` in der CI nicht scheitern.
- `dotnet.defaultSolution` lädt `src/backend/Murmli.Backend.slnx` in C# Dev Kit, ohne Nachfrage beim Öffnen.
- `files.exclude` blendet `*.g.dart`, `*.gr.dart` und `*.freezed.dart` im Explorer und in der Suche aus. Sie sind generiert und nicht committet.

## Extensions

`extensions.json` empfiehlt Flutter (bringt Dart mit), Aspire und C# Dev Kit, VS Code schlägt sie beim Öffnen des Repos vor.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `App: build_runner` als `preLaunchTask` der App | Generierte Dateien werden nicht committet ([AGENTS.md](../AGENTS.md#ci)), ohne sie startet die App nach einem frischen Checkout nicht. Kostet bei jedem Start einige Sekunden. |
