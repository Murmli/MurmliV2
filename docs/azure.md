# Aspire und Azure

Betriebsdoku (Einrichtung, Deploy-Ablauf): [BackOffice, docs/03_Betrieb/Deployment.md](https://github.com/Murmli/BackOffice/blob/main/docs/03_Betrieb/Deployment.md).

## Umgebungen und Deploy

- Umgebungen heißen `azure-<name>`, derzeit `azure-dev` in `rg-murmli-dev`.
- Dev deployt nur `deploy-backend.yml` über das GitHub-Environment `development` (nur `main`): OIDC als Managed Identity mit Contributor und einem auf AcrPull beschränkten RBAC Administrator auf `rg-murmli-dev`.
- `deploy-backend.yml` liest die Aspire-CLI-Version aus dem `Sdk`-Attribut des AppHost-csproj.

## Secret-Parameter

`better-auth-secret` und `auth-service-key` (Bedeutung: [authentication.md](authentication.md#konfiguration)) sind AppHost-Parameter mit `secret: true`, in Azure je ein Container-App-Secret.

- Dev: GitHub-Secrets `BETTER_AUTH_SECRET` und `AUTH_SERVICE_KEY` im Environment `development`, `ci.yml` reicht sie ausdrücklich an `deploy-backend.yml`. Ein fehlendes Secret käme als leerer String an, den Aspire akzeptiert, deshalb bricht `deploy-backend.yml` vorher ab.
- Lokal und in Tests: [AGENTS.md](../AGENTS.md#aspire-und-azure).

## Anmeldung beim Deploy (Aspire 13.6.5)

- Aspire meldet sich nur über die `az`-Session an, `AZURE_CLIENT_ID`, `AZURE_FEDERATED_TOKEN_FILE` und Workload Identity wirken nicht.
- Die OIDC-Assertion gilt nur fünf Minuten, deshalb holt der Workflow die Tokens direkt nach dem Login ([microsoft/aspire#19560](https://github.com/microsoft/aspire/issues/19560)). Resource-Strings mit `/` am Ende verfehlen den Cache, eine fehlende Audience endet in AADSTS700024.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `aspire run`/`dotnet test` nur mit lokalen Ressourcen | Sonst legen sie echte Azure-Ressourcen an. |
| Subscription-ID nur als Secret | Das Repo ist öffentlich. |
| `Azure:*`-Werte nur in `appsettings.azure-<name>.json` | Sonst bindet sie auch `aspire run`. |
| Bis zu zwei Wiederholungen von `aspire deploy` | Aspire provisioniert pro App mit Datenbankzugriff (derzeit `api`, `auth`, `product-import`) parallel ein eigenes Postgres-Admin-Modul, Flexible Server lehnt das mitunter mit `ServerIsBusy` ab. Eine Wiederholung nutzt den Deploy-Stand auf dem Runner und wiederholt nur gescheiterte Module. |
| `shell: bash` am Step „Deploy“ | Ohne startet der Runner `bash -e` ohne `-o pipefail`, der Step meldete dann ein gescheitertes `git ls-remote` als „Skipped“ und endete grün. |
| `~/.aspire/deployments` nie cachen oder hochladen | Enthält Subscription und Ressourcen-IDs. |
| Rollenzuweisungen und Resource Provider erst nach Vorbereitung in Azure | Sonst scheitert der Deploy. Application Insights braucht `Microsoft.Insights`, ein Mensch registriert ihn vor dem ersten Deploy. |
| Registry im RBAC-Modus | Verworfen: ABAC-Modus, dort scheitern Push und Pull. |
| Ressourcennamen und Naming-Schema bleiben | Eine Umbenennung legt neue, leere Ressourcen an. Aspire löscht beim Deploy nie etwas, die alten kosten weiter. |
| Gemeinsame Identity `backend-identity` | Aspire macht jede Identity zu einem eigenen Postgres-Entra-Admin mit eigener Rolle, Tabellen gehören der migrierenden Rolle. Verworfen: getrennte Identities, gegenseitiger Zugriff ginge nur über Azure-spezifisches Verhalten von `azure_pg_admin` und wäre lokal nicht testbar. Auch `auth` hat sie, sein Modul `auth-roles-postgres` gleicht denen der anderen Apps (derselbe Principal) und brauchte keine Vorbereitung in Azure. |
| Ausgeschlossene Azure-Features | Preview oder Zusatzkosten. |

Verworfene Wege für Telemetrie (OpenTelemetry-Agent der Umgebung, OTLP-Aufnahme von Azure Monitor): [telemetry.md](telemetry.md#entscheidungen).
