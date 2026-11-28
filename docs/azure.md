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

## Custom Domain

Der AppHost hängt `api.dev.murmli.de` per `ConfigureCustomDomain` an `api`, aus den Parametern `api-custom-domain` und `api-certificate-name`. Er legt sie nur im Publish-Mode an, sonst fragt `aspire run` nach Werten. Die Werte stehen in `appsettings.azure-dev.json`, sie sind nicht geheim. Den Zertifikatsnamen übergibt der AppHost als festen Wert aus `Parameters:api-certificate-name` (leer, wenn er fehlt): Aspire 13.6.5 wertet einen leeren Wert unter `Parameters:<name>` als fehlend (`MissingParameterValueException`), ein Deploy ohne Interaktion scheitert dann. Ein leerer Zertifikatsname erzeugt eine deaktivierte Bindung (`bindingType` `Disabled`), ein gesetzter eine Bindung per SNI an das gleichnamige Managed Certificate der Umgebung.

Ein Managed Certificate lässt sich erst anlegen, wenn der Hostname an der App hängt. Eine neue Domain braucht deshalb zwei Deploys:

1. Ein Mensch legt die DNS-Einträge an: CNAME auf die Standard-Domain der App und TXT `asuid.<Hostname ohne Zone>` mit der Verification-ID der Umgebung.
2. Deploy mit leerem Zertifikatsnamen, der Hostname hängt ohne Zertifikat an der App.
3. Ein Mensch legt im Portal an der App unter „Custom domains“ per „Add binding“ das Managed Certificate an.
4. Deploy mit dessen Namen als Zertifikatsname.

Bis zum Deploy aus Schritt 4 setzt jeder Deploy die Bindung aus Schritt 3 wieder auf `Disabled`. Das Managed Certificate bleibt in der Umgebung, der Deploy aus Schritt 4 bindet es wieder.

## Anmeldung beim Deploy (Aspire 13.6.5)

- Aspire meldet sich nur über die `az`-Session an, `AZURE_CLIENT_ID`, `AZURE_FEDERATED_TOKEN_FILE` und Workload Identity wirken nicht.
- Die OIDC-Assertion gilt nur fünf Minuten, deshalb holt der Workflow die Tokens direkt nach dem Login ([microsoft/aspire#19560](https://github.com/microsoft/aspire/issues/19560)). Resource-Strings mit `/` am Ende verfehlen den Cache, eine fehlende Audience endet in AADSTS700024.

## Aufräumen des Abgleichs

Abgelaufene Löschmarkierungen und Kommando-IDs entfernt der Hintergrunddienst `SyncCleanupService` in der API ([sync.md](sync.md#aufbewahrung-und-aufräumen)), ohne eigene Ressource; derselbe Lauf schließt hängengebliebene Kontolöschungen ab ([authentication.md](authentication.md#konto-löschen)). Er läuft eine Minute nach jedem Start der API und danach alle 24 Stunden, solange die Instanz läuft. In Dev, wo die API auf null Instanzen herunterfährt, läuft er also bei jedem Kaltstart und täglich nur während der Nutzung; ohne Nutzung wächst dort nichts nach. Mehrere Instanzen und die Revisionen eines Deploys stören sich nicht, die Lease in `sync.cleanup_lease` lässt höchstens einen Lauf zu. `Sync:Cleanup:Enabled=false` schaltet ihn ab; derzeit setzt keine Umgebung einen `Sync:Cleanup:*`-Wert.

## Sicherung einspielen

Nach dem Einspielen einer Sicherung erneuert ein Mensch die Kennung der Datenbank mit `UPDATE sync.database_identity SET value = gen_random_uuid();` ([database.md](database.md#abgleich)), sonst verpassen Geräte Änderungen, deren Versionen ihr Token schon zu kennen glaubt. Den Zugang zur Datenbank beschreibt die Betriebsdoku (oben, Abschnitt „Eigener Zugang zur Datenbank“).

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `aspire run`/`dotnet test` nur mit lokalen Ressourcen | Sonst legen sie echte Azure-Ressourcen an. |
| Subscription-ID nur als Secret | Das Repo ist öffentlich. |
| `Azure:*`-Werte nur in `appsettings.azure-<name>.json` | Sonst bindet sie auch `aspire run`. |
| Custom Domain im AppHost statt nur im Portal | Jeder Deploy wendet die Container App vollständig neu an und entfernt eine nur im Portal angelegte Domain samt Bindung. |
| Bis zu zwei Wiederholungen von `aspire deploy` | Aspire provisioniert pro App mit Datenbankzugriff (derzeit `api`, `auth`, `product-import`) parallel ein eigenes Postgres-Admin-Modul, Flexible Server lehnt das mitunter mit `ServerIsBusy` ab. Eine Wiederholung nutzt den Deploy-Stand auf dem Runner und wiederholt nur gescheiterte Module. |
| `shell: bash` am Step „Deploy“ | Ohne startet der Runner `bash -e` ohne `-o pipefail`, der Step meldete dann ein gescheitertes `git ls-remote` als „Skipped“ und endete grün. |
| `~/.aspire/deployments` nie cachen oder hochladen | Enthält Subscription und Ressourcen-IDs. |
| Rollenzuweisungen und Resource Provider erst nach Vorbereitung in Azure | Sonst scheitert der Deploy. Application Insights braucht `Microsoft.Insights`, ein Mensch registriert ihn vor dem ersten Deploy. |
| Registry im RBAC-Modus | Verworfen: ABAC-Modus, dort scheitern Push und Pull. |
| Ressourcennamen und Naming-Schema bleiben | Eine Umbenennung legt neue, leere Ressourcen an. Aspire löscht beim Deploy nie etwas, die alten kosten weiter. |
| Gemeinsame Identity `backend-identity` | Aspire macht jede Identity zu einem eigenen Postgres-Entra-Admin mit eigener Rolle, Tabellen gehören der migrierenden Rolle. Verworfen: getrennte Identities, gegenseitiger Zugriff ginge nur über Azure-spezifisches Verhalten von `azure_pg_admin` und wäre lokal nicht testbar. Auch `auth` hat sie, sein Modul `auth-roles-postgres` gleicht denen der anderen Apps (derselbe Principal) und brauchte keine Vorbereitung in Azure. |
| Ausgeschlossene Azure-Features | Preview oder Zusatzkosten. |
| Aufräumen des Abgleichs als Hintergrunddienst in der API | Keine neue Ressource und kein neuer Ressourcenname, kein weiteres Postgres-Admin-Modul je Deploy (`ServerIsBusy`), kein eigener Verbindungspool neben den 35 Verbindungen des Servers. Dass er in Dev nur läuft, während die API läuft, ist unschädlich, ohne Nutzung wächst nichts nach. Verworfen: geplanter Container-Apps-Job. |

Verworfene Wege für Telemetrie (OpenTelemetry-Agent der Umgebung, OTLP-Aufnahme von Azure Monitor): [telemetry.md](telemetry.md#entscheidungen).
