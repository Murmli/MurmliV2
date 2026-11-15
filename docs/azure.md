# Aspire und Azure

Betriebsdoku (Einrichtung, Deploy-Ablauf): [BackOffice, docs/03_Betrieb/Deployment.md](https://github.com/Murmli/BackOffice/blob/main/docs/03_Betrieb/Deployment.md).

## Umgebungen und Deploy

- Umgebungen heißen `azure-<name>`, derzeit `azure-dev` in `rg-murmli-dev`.
- `aspire publish` und `aspire deploy` immer mit `--environment azure-<name>`. Ohne läuft der AppHost als Production, dafür gibt es keine Zitadel-Werte und `WithZitadel()` bricht ab.
- Dev deployt nur `deploy.yml`: GitHub-Environment `development` (nur `main`), OIDC als Managed Identity mit Contributor und einem auf AcrPull beschränkten RBAC Administrator auf `rg-murmli-dev`.
- `deploy.yml` liest die Aspire-CLI-Version aus dem `Sdk`-Attribut (`Aspire.AppHost.Sdk`) des AppHost-csproj.

## Secret-Parameter

- `zitadel-service-user-token` (PAT des Zitadel-Service-Users, siehe [authentication.md](authentication.md)) ist ein AppHost-Parameter mit `secret: true`.
- Dev: GitHub-Secret `ZITADEL_SERVICE_USER_TOKEN` im Environment `development`. `ci.yml` reicht es an `deploy.yml`, dort kommt es als `Parameters__zitadel_service_user_token` an den AppHost. Ein fehlendes Secret käme als leerer String an, den Aspire akzeptiert, deshalb bricht `deploy.yml` vorher ab. In Azure wird daraus ein Container-App-Secret.
- Lokal: User-Secret `Parameters:zitadel-service-user-token` des AppHost. Ohne Wert startet die API nicht, ein Dummy-Wert reicht, solange man die Löschung nicht lokal testet. Aspire-Tests, die den AppHost starten, übergeben einen Dummy als Argument.

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
| Rollenzuweisung, Resource Provider erst nach Vorbereitung in Azure | Sonst scheitert der Deploy. Application Insights braucht den Resource Provider `Microsoft.Insights`, ein Mensch registriert ihn vor dem ersten Deploy. |
| Registry im RBAC-Modus | Verworfen: ABAC-Modus, dort scheitern Push und Pull. |
| Ressourcennamen und Naming-Schema bleiben | Eine Umbenennung legt in Azure neue, leere Ressourcen an, die alten bleiben und kosten weiter. Aspire löscht beim Deploy nie etwas. |
| Gemeinsame Identity `backend-identity` | Aspire macht jede Identity zu einem eigenen Postgres-Entra-Admin und damit zu einer eigenen Rolle, Tabellen gehören der Rolle, die migriert. Verworfen: getrennte Identities, sie verteilten die Tabellen auf mehrere Rollen, deren gegenseitiger Zugriff nur über Azure-spezifisches Verhalten von `azure_pg_admin` geht und lokal nicht testbar ist. |
| Ausgeschlossene Azure-Features | Preview oder Zusatzkosten. |
| Keine Container-Apps-Probes | Gilt bis zum ersten konkreten Bedarf. |
| Kein verwalteter OpenTelemetry-Agent der Container-Apps-Umgebung | Aspire 13.6.5 hat dafür keine API, der veraltete Collector bildet die Daten falsch ab. Siehe [Murmli/BackOffice#7](https://github.com/Murmli/BackOffice/issues/7). |
| Keine native OTLP-Aufnahme von Azure Monitor | Braucht einen eigenen Collector mit Entra-Anmeldung und eine Rollenzuweisung und schreibt in andere Tabellen. Siehe [Murmli/BackOffice#7](https://github.com/Murmli/BackOffice/issues/7). |
| Kein klassisches Application-Insights-SDK | Abgekündigt. |
