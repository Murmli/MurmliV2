# Aspire und Azure

Betriebsdoku (Einrichtung, Deploy-Ablauf): [BackOffice, docs/03_Betrieb/Deployment.md](https://github.com/Murmli/BackOffice/blob/main/docs/03_Betrieb/Deployment.md).

## Umgebungen und Deploy

- Umgebungen heißen `azure-<name>`, derzeit `azure-dev` in `rg-murmli-dev`.
- Dev deployt nur `deploy.yml`: GitHub-Environment `development` (nur `main`), OIDC als Managed Identity mit Contributor und einem auf AcrPull beschränkten RBAC Administrator auf `rg-murmli-dev`.
- `deploy.yml` liest die Aspire-CLI-Version aus dem `Sdk`-Attribut (`Aspire.AppHost.Sdk`) des AppHost-csproj.

## Anmeldung beim Deploy (Aspire 13.6.5)

- Aspire meldet sich nur über die `az`-Session an. `AZURE_CLIENT_ID`, `AZURE_FEDERATED_TOKEN_FILE` und Workload Identity wirken nicht.
- Die OIDC-Assertion gilt nur fünf Minuten, deshalb holt der Workflow die Tokens direkt nach dem Login ([microsoft/aspire#19560](https://github.com/microsoft/aspire/issues/19560)). Resource-Strings mit Schrägstrich am Ende verfehlen den Cache.
- Braucht Aspire eine weitere Audience, scheitert der Deploy mit AADSTS700024.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `aspire run`/`dotnet test` nur mit lokalen Ressourcen | Sonst legen sie echte Azure-Ressourcen an. |
| Subscription-ID per Secret | Das Repo ist öffentlich. |
| `Azure:*`-Werte nur in `appsettings.azure-<name>.json` | Sonst bindet sie auch `aspire run`. |
| Einmaliger Retry von `aspire deploy` | Aspire provisioniert pro App mit Datenbankzugriff ein eigenes Postgres-Admin-Modul, die Module laufen parallel, Flexible Server kann das mit `ServerIsBusy` ablehnen. Der zweite Versuch nutzt den Deploy-Stand auf dem Runner und wiederholt nur gescheiterte Module. |
| `~/.aspire/deployments` nie cachen oder hochladen | Enthält Subscription und Ressourcen-IDs. |
| Rollenzuweisung, Resource Provider erst nach Vorbereitung in Azure | Sonst scheitert der Deploy. |
| Registry im RBAC-Modus | Verworfen: ABAC-Modus, dort scheitern Push und Pull. |
| Ressourcennamen und Naming-Schema bleiben | Eine Umbenennung legt in Azure neue, leere Ressourcen an, die alten bleiben und kosten weiter. Aspire löscht beim Deploy nie etwas. |
| Gemeinsame Identity `backend-identity` | Aspire macht jede Identity zu einem eigenen Postgres-Entra-Admin und damit zu einer eigenen Rolle, Tabellen gehören der Rolle, die migriert. Verworfen: getrennte Identities, sie verteilten die Tabellen auf mehrere Rollen, deren gegenseitiger Zugriff nur über Azure-spezifisches Verhalten von `azure_pg_admin` geht und lokal nicht testbar ist. |
| Ausgeschlossene Azure-Features | Preview oder Zusatzkosten. |
| Keine Container-Apps-Probes, keine ServiceDefaults | Ohne Dashboard exportiert OpenTelemetry nichts. Gilt bis zum ersten konkreten Bedarf. |
