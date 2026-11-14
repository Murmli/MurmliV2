# CI

`ci.yml` läuft bei Push auf `main` und bei Pull Requests und ruft `lint.yml`, `test.yml`, `build-app.yml` und `vulnerability-scan.yml` auf, auf `main` danach `deploy.yml` ([azure.md](azure.md)).

Lokale Checks (Befehle in [AGENTS.md](../AGENTS.md#ci)):

- `dotnet test` braucht Docker und beim ersten Build Netzwerk für `dnx aspire.cli`, sonst ASPIRE009.
- `dotnet restore` meldet neue Advisories nur mit `--force`.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `main` geschützt, Ready for review und Merge nur durch einen Menschen | Jeder Merge auf `main` deployt Dev. |
| Concurrency nur in `ci.yml` | In Called Workflows blockiert oder cancelt sie den Caller. |
| Gruppe `deploy-dev` am Job, ohne Cancel | Ein Abbruch bräche laufende ARM-Deployments ab. |
| Keine Gruppe am aufrufenden `deploy`-Job | Sonst wartet der Deploy auf eine Sperre, die sein eigener Caller hält. |
