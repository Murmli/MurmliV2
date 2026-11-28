# Telemetrie

`Murmli.Backend.Telemetry` (`AddTelemetry()`) richtet OpenTelemetry für die .NET-Hosts `api` und `product-import` ein, `auth` hat ein eigenes Setup (siehe [`auth`](#auth)).

## Was aufgezeichnet wird

Quellen und Meter stehen in `AddTelemetry()`, die von `Npgsql` registrieren die Aspire-Integrationen selbst. Npgsql-Spans enthalten nie Parameterwerte.

### Konto-Id

`UserTelemetryMiddleware` läuft nach Authentifizierung und Autorisierung ([api.md](api.md#pipeline)), sucht zu `sub` die eigene Id in `identity.users` (und schreibt höchstens einmal am Tag den Tag der letzten Anfrage, [authentication.md](authentication.md#datenmodell)) und setzt sie, wenn es ein Konto gibt, als `UserId` am Request-Span und als Log-Scope für den Rest der Anfrage (auch an EF-Core-Command-Logs und `UnhandledException`). Suche in Application Insights:

```kusto
union requests, traces, exceptions
| where tostring(customDimensions.UserId) == "<Konto-Id>"
```

Ohne `UserId` bleiben:

- Anfragen ohne Anmeldung oder ohne Konto (nie `GET /v1/users/me` aufgerufen, Konto gelöscht), der Produktimport und `auth`.
- Der erste `GET /v1/users/me`, der das Konto anlegt: nur der Log `UserCreated` trägt die Id.
- Kind-Spans (Npgsql, HttpClient), sie hängen über die Trace-ID am Request.
- Logs vor der Middleware und die ihrer eigenen Anweisungen (Authentifizierung, die Abfrage der Konto-Id und das Schreiben des Tags der letzten Anfrage samt `DatabaseUnavailable`, wenn eine davon an einer Störung der Datenbank scheitert).
- Der Aufräum-Lauf des Abgleichs samt seinem Span `sync cleanup run`, er läuft ohne Anfrage. Ausgenommen ist das Abschließen hängengebliebener Kontolöschungen: Es loggt je Konto mit `UserId` als Log-Scope ([authentication.md](authentication.md#konto-löschen)).
- Metriken, ihre Tags tragen nie Ids.

### Abgleich

Der Kern zeichnet über `SyncTelemetry` auf (Meter und `ActivitySource` `Murmli.Backend.Sync`, den Meter aus `IMeterFactory`), die API zählt abgewiesene Anfragen über `ApiTelemetry` (Meter `Murmli.Backend.Api`). Instrumente und Tags heißen im Stil von OpenTelemetry (`murmli.sync.*`), Span-Attribute und Log-Properties in PascalCase aus `LogProperties` wie `UserId`. Werte von Tags stammen nur aus festen Mengen des Servers: Status, Gründe und Ursachen aus [sync.md](sync.md#ergebnisse) unverändert (`none`, wenn ein Ergebnis keins von beiden hat), Kommandoart und Datentyp nur, wenn der Server sie kennt, sonst `unknown`.

| Instrument | Art, Einheit | Tags | Aufgezeichnet |
| --- | --- | --- | --- |
| `murmli.sync.commands` | Counter `{command}` | `murmli.sync.command.status`, `murmli.sync.command.reason`, `murmli.sync.command.kind`, `murmli.sync.data_type`, `murmli.sync.writer` (`device` oder `server`) | je Ergebnis eines Kommandos eines Geräts oder des Servers als Schreiber, auch `alreadyProcessed` (Grund `none`) und `failed`; nicht Kommandos ohne Ergebnis und nicht die der Beiträge zum ersten Abgleich |
| `murmli.sync.request.commands` | Histogram `{command}` | – | je Anfrage, die der Kern verarbeitet: Zahl ihrer Kommandos, auch 0 |
| `murmli.sync.device.pending_commands` | Histogram `{command}` | – | `pendingCommandCount` der Anfrage, wenn vorhanden und nicht negativ |
| `murmli.sync.device.oldest_pending_age` | Histogram `s` | – | `oldestPendingCommandAgeSeconds` der Anfrage, ebenso |
| `murmli.sync.response.size` | Histogram `By` | `murmli.sync.response.compressed` | je Antwort mit 200: Größe des JSON vor gzip |
| `murmli.sync.response.compressed_size` | Histogram `By` | – | je Antwort mit gzip: gesendete Größe |
| `murmli.sync.full_state.scopes` | Counter `{scope}` | `murmli.sync.full_state.reason` (Tabelle unten) | je Bereich mit vollständigem Stand |
| `murmli.sync.scopes.removed` | Counter `{scope}` | `murmli.sync.removed_by_own_account` | je Bereich in `removedScopes` |
| `murmli.sync.requests.rejected` | Counter `{request}` | `murmli.sync.rejection.reason`: `account_gone` (410), `invalid_request` (400, 415 ohne `Content-Type`), `request_too_large` (413), `rate_limited_device`, `rate_limited_account`, `rate_limited_commands` (429), `service_unavailable` (503) | je Anfrage, die `/v1/sync` selbst abweist; nicht die 401 und 403 der Anmeldung, die 415 des Routings und die 503 der `UserTelemetryMiddleware` (Log `DatabaseUnavailable`) |
| `murmli.sync.cleanup.runs` | Counter `{run}` | `murmli.sync.cleanup.result`: `succeeded` (auch wenn ein Beitrag mit einer Exception scheitert, die keine Störung der Datenbank ist), `stopped` (an der Zeitgrenze), `failed`, `skipped` | je Aufräum-Lauf |
| `murmli.sync.cleanup.duration` | Histogram `s` | – | je Lauf außer `skipped` |
| `murmli.sync.cleanup.removed` | Counter `{row}` | `murmli.sync.cleanup.kind`: `tombstones`, `processed_commands` und der `Name` jedes Beitrags | je Lauf außer `skipped`, auch mit 0: `tombstones` und `processed_commands` immer, jeder Beitrag, den der Lauf startet, mit dem, was er über `SyncCleanupContext.AddRemovedRecords` gemeldet hat – läuft er durch, alles, wirft er (auch mit einer Störung der Datenbank) oder bricht ihn die Zeitgrenze oder der Aufrufer ab, alles bis dahin; Beiträge, die der Lauf nicht mehr startet, fehlen; `stale_account_deletions` zählt immer 0, abgeschlossene Kontolöschungen zeigt `StaleAccountDeletionCompleted` (2004) |
| `murmli.sync.cleanup.last_success_time` | ObservableGauge `s` | – | Uhr des letzten Laufs mit `succeeded` dieser Instanz als Unix-Sekunden (nicht `stopped`), nur im Speicher; ohne erfolgreichen Lauf kein Wert |

| `murmli.sync.full_state.reason` | Vollständiger Stand, weil ([sync.md](sync.md#vollständiger-stand-und-änderungen)) |
| --- | --- |
| `no_token` | die Anfrage kein Token hat |
| `unreadable_token` | das Token nicht lesbar ist |
| `database_identity` | die Kennung der Datenbank im Token abweicht |
| `root_delete_not_applied` | ein `delete` des Wurzel-Datensatzes in dieser Anfrage nicht angewendet wurde |
| `unknown_scope` | das Token den Bereich nicht nennt |
| `epoch` | die Epoche abweicht |
| `below_floor` | die Version im Token unter der Untergrenze liegt |
| `ahead_of_server` | die Version im Token über der des Servers liegt |
| `too_many_deletions` | über der Version im Token mehr Löschmarkierungen liegen als die Schwelle |

Treffen mehrere Gründe zu, zählt der erste der Tabelle.

Ohne eigenen Code kommen die Dauer der Anfragen (`http.server.request.duration`, in Application Insights die Tabelle `requests` mit `POST /v1/sync`) und die Pool-Metriken von Npgsql (`db.client.connection.count` mit `db.client.connection.state`, `db.client.connection.max`, `db.client.connection.npgsql.pending_requests`, `db.client.connection.npgsql.timeouts`); ein eigenes Instrument für die Dauer gibt es nicht.

Am Request-Span von `POST /v1/sync` setzt der Kern `DeviceId`, `SyncCommandCount` und, wenn das Gerät sie schickt und sie nicht negativ sind, `SyncPendingCommandCount` und `SyncOldestPendingCommandAgeSeconds`; bei einer Antwort mit 200 zusätzlich `SyncProcessedCommandCount` (Kommandos mit Ergebnis), `SyncFullStateScopeCount`, `SyncRemovedScopeCount`, `SyncResponseBytes` (JSON vor gzip) und `SyncCompressed`. Kind-Spans je Phase gibt es nicht, die Zeiten der Datenbank zeigen die Npgsql-Spans. Application Insights speichert von Histogrammen nur Summe, Anzahl, Minimum und Maximum; Perzentile des Rückstands der Geräte kommen deshalb aus den Span-Attributen:

```kusto
requests
| where name == "POST /v1/sync"
| summarize percentile(toint(customDimensions["SyncPendingCommandCount"]), 95), percentile(toint(customDimensions["SyncOldestPendingCommandAgeSeconds"]), 95) by bin(timestamp, 1h)
```

| Id | Event | Level | Properties | Wann |
| --- | --- | --- | --- | --- |
| 3000 | `SyncCommandRejected` | Information | `DeviceId`, `CommandId`, `DataType`, `CommandKind`, `ScopeId`, `RecordId`, `Reason` | je Ergebnis `rejected` eines Kommandos eines Geräts; ein gespeichertes Kommando nicht erneut als `alreadyProcessed`, ein ungültiges ohne lesbare Pflichtfelder (nicht gespeichert, [sync.md](sync.md#ergebnisse)) bei jeder Wiederholung erneut; Ids nur, soweit lesbar, `DataType` und `CommandKind` nur, wenn der Server sie kennt |
| 3001 | `SyncScopeRemoved` | Information | `DeviceId`, `ScopeId`, `RemovedByOwnAccount` | je Bereich in `removedScopes` einer Antwort, höchstens 100 je Antwort (`murmli.sync.scopes.removed` zählt alle) |
| 3002 | `SyncCommandFailed` | Error mit Exception | `DeviceId`, `CommandId`, `DataType`, `CommandKind` | ein einzelnes Kommando scheitert; beim Server als Schreiber ([sync.md](sync.md#server-als-schreiber)) ohne `DeviceId`; `DataType` und `CommandKind` nur, wenn der Server sie kennt |
| 3003 | `SyncDatabaseUnavailable` | Warning mit Exception | `Phase`: `bootstrap`, `commands` oder `changes` | eine Störung der Datenbank beendet die Anfrage mit 503 |
| 3004 | `SyncAccountBootstrapped` | Information | `ScopeId` | der erste Abgleich eines Kontos legt den persönlichen Bereich an |
| 3005 | `SyncCleanupCompleted` | Information | `DurationMilliseconds`, `RemovedTombstoneCount`, `RemovedProcessedCommandCount`, `RemovedRecordCount` | Ende eines vollständigen Aufräum-Laufs ([sync.md](sync.md#aufbewahrung-und-aufräumen)) |
| 3006 | `SyncCleanupSkipped` | Information | – | ein anderer Lauf hält die Lease |
| 3007 | `SyncCleanupFailed` | Error mit Exception | – | ein Beitrag oder der ganze Lauf scheitert, sein Aufrufer bricht ihn ab oder die Lease lässt sich nicht freigeben (dann auch nach `SyncCleanupCompleted` oder `SyncCleanupStopped`; die Lease läuft nach `Sync:Cleanup:LeaseDuration` ab) |
| 3008 | `SyncCleanupStopped` | Warning | `DurationMilliseconds`, `RemovedTombstoneCount`, `RemovedProcessedCommandCount`, `RemovedRecordCount` | ein Aufräum-Lauf endet an seiner Zeitgrenze; den Rest erledigt der nächste Lauf |

Logs innerhalb einer Anfrage tragen `UserId` aus dem Scope der `UserTelemetryMiddleware`, der Kern setzt sie nie selbst; die Logs des Aufräum-Laufs sind ohne `UserId`, außer beim Abschließen hängengebliebener Kontolöschungen ([Konto-Id](#konto-id)). Übernommene Kommandos loggt der Server nicht einzeln, die Logs ergeben keinen Verlauf, wer wann was geändert hat ([#138](https://github.com/Murmli/MurmliV2/issues/138)).

Der Aufräum-Lauf öffnet den Span `sync cleanup run` (Quelle `Murmli.Backend.Sync`), unter dem die Npgsql-Spans des Laufs hängen; scheitert der Lauf (`failed`), steht sein Status auf `Error`, an der Zeitgrenze (`stopped`) bleibt er ungesetzt. Den Alarm „kein erfolgreicher Aufräum-Lauf seit 2 Tagen“ richtet ein Mensch für Produktion über `SyncCleanupCompleted` ein, das auch auffindbar ist, wenn gerade keine Instanz läuft; Läufe, die an ihrer Zeitgrenze enden (`SyncCleanupStopped`), zählen dafür nicht; Abfragen und Alarme gehören in die Betriebsdoku im BackOffice.

Eigener Code schreibt nie Feldwerte, Anzeigenamen, das Token der Anmeldung, `sub`, das Token des Abgleichs oder Bodys in Logs, Span-Attribute, Metrik-Tags oder Meldungen von Exceptions; Ids von Bereichen, Datensätzen, Kommandos und Geräten nur in Logs und Span-Attribute. Inhalte erscheinen nur unter `Telemetry:RecordSensitiveData` in den EF-Core-Command-Logs ([Sensible Daten](#sensible-daten)).

## Export

OTLP nur mit `OTEL_EXPORTER_OTLP_ENDPOINT` (setzt Aspire im Run-Mode), Azure Monitor nur mit `APPLICATIONINSIGHTS_CONNECTION_STRING` (nur in Azure, über `WithTelemetry(applicationInsights)`). Ohne beides wird aufgezeichnet, aber nicht exportiert.

## Sensible Daten

`Telemetry:RecordSensitiveData` (Standard aus) schaltet LLM-Inhalte (`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`) und SQL-Parameterwerte in den EF-Core-Command-Logs (`EnableSensitiveDataLogging`) ein. An ist er nur lokal und in Azure Dev, wo es nur Testdaten gibt (`appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost). Ausnahme für Dev ([#138](https://github.com/Murmli/MurmliV2/issues/138)): Der Sync-Kern schreibt Datensätze über EF Core, mit dem Schalter tragen die EF-Core-Command-Logs deshalb auch Inhalte abgeglichener Datensätze als SQL-Parameterwerte, in Azure Dev bis in Application Insights. Eigener Code loggt nie Inhalte. Ohne den Schalter (Standard und jede andere Umgebung) loggt EF Core keine Parameterwerte, und Npgsql-Spans tragen nur das SQL mit Platzhaltern. `SyncEndpointsTelemetryTests` prüft beides über alle Vorgänge des Abgleichs: Ohne den Schalter stehen Feldwerte, Anzeigenamen, `sub` und Tokens in keinem Log, keinem Span und keiner Metrik, mit ihm Feldwerte und `sub` nur in Logs der Kategorie `Microsoft.EntityFrameworkCore.Database.Command`.

## `auth`

- Nur Traces (HTTP ein und aus, `fetch`, `pg`, Spans von Better Auth mit Scope `better-auth`), keine OpenTelemetry-Metriken und -Logs. Better Auth loggt ab `warn` auf die Konsole, Container Apps schreibt sie in den Workspace.
- Ausgehende Spans über `node:http`/`node:https` tragen die URL samt Query, geschwärzt werden nur Zugangsdaten und einige Signatur-Parameter (etwa `sig`, `X-Amz-Signature`). In Azure steht so die `client_id` von `backend-identity` im Span des Token-Abrufs von `@azure/identity`.
- Eingehende Spans tragen die Query mit derselben Einschränkung in `url.query` (bei einem OAuth-Callback `code` und `state`). Der Azure-Monitor-Exporter übernimmt sie nicht, im lokalen Dashboard steht sie.
- `fetch`-Spans: URL ohne Query, von den Headern nur `User-Agent`, nie der Body. Über `fetch` ruft Better Auth Anmeldeanbieter auf, derzeit bei jeder Anmeldung mit Apple bzw. Google deren Schlüssel.
- `src/auth/src/main.ts` registriert nur die Hooks von import-in-the-middle und importiert Telemetrie und Server danach dynamisch: `node:http` wird nur instrumentiert, wenn die Hooks vor seinem ersten Import registriert sind, die Instrumentierung patcht `pg` beim Laden. `fetch` braucht keine Hooks (`diagnostics_channel`).
- Export, Propagator (W3C Trace Context ohne Baggage), Sampler und `/health`-Filter wie bei .NET, `pg`-Spans ohne Parameterwerte.
- Node läuft im Container als PID 1 und beendet sich bei SIGTERM und SIGINT nur über den eigenen Handler, der vorher die gepufferten Spans schreibt.
- `Telemetry__RecordSensitiveData` und `Logging__Console__LogLevel__Default` reicht der AppHost auch an `auth`, das sie nicht liest (harmlos).

## Tests

- Meter- und Span-Listener von `ApiHost` sind prozessweit ([Entscheidungen](#entscheidungen)): Metriken und Spans eines Hosts enthalten auch die parallel laufender Hosts. Tests in `Murmli.Backend.Api.Tests`, die Metriken auswerten oder Spans zählen bzw. ihr Vorhandensein erwarten, ohne auf die eigene Trace-ID im `traceparent` oder bei Npgsql-Spans auf die eigene Datenbank in `db.namespace` zu filtern, laufen deshalb in `[Collection<TelemetryCollection>]` ohne Parallelität. Prüfungen, dass etwas in keinem Span steht, dürfen überall alle Spans durchsuchen.
- Metriken sind erst nach `ApiHost.FlushMetrics()` vollständig, Spans und Logs erst nach dem Entsorgen des Hosts. Mit `metricSnapshots` exportiert `ApiHost` per Delta-Temporalität, ein Test wertet nur die Snapshots nach einem Flush aus (`TestMetrics`).

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Kein ServiceDefaults-Template | Es hängt an jeden HttpClient einen Resilience-Handler, der den Download des Produktexports abbräche, die API zum Entfernen ist experimentell. |
| Keine Instrumentation-Pakete | ASP.NET Core und HttpClient erzeugen unter .NET 11 selbst Activities nach den Semantic Conventions. Das ASP.NET-Core-Paket zöge ASP.NET Core in den Worker des Produktimports. |
| Nur die Exporter (`Azure.Monitor.OpenTelemetry.Exporter`, in `auth` `@azure/monitor-opentelemetry-exporter`), nicht die Distros | Instrumentierung, Sampler und Propagator legen wir selbst fest. Die .NET-Distro zöge ASP.NET-Core- und SqlClient-Instrumentierung in den Worker und schaltet das Schwärzen von Query-Parametern in URLs ab. Verworfen auch: das klassische Application-Insights-SDK (abgekündigt). |
| Kein verwalteter OpenTelemetry-Agent der Container-Apps-Umgebung | Aspire 13.6.5 hat dafür keine API, der veraltete Collector bildet die Daten falsch ab ([Murmli/BackOffice#7](https://github.com/Murmli/BackOffice/issues/7)). |
| Keine native OTLP-Aufnahme von Azure Monitor | Braucht einen eigenen Collector mit Entra-Anmeldung und Rollenzuweisung und schreibt in andere Tabellen ([Murmli/BackOffice#7](https://github.com/Murmli/BackOffice/issues/7)). |
| Sampler `ParentBased` mit `HealthCheckExcludingSampler` für Root und entfernte Parents | Jeder Request außer `/health` ist über seine Trace-ID auffindbar, auch mit Sampled-Flag `00` vom Aufrufer (`ParentBased(AlwaysOn)` verwürfe ihn). Reines `AlwaysOn` ignorierte den Parent, die Npgsql-Spans des Health-Checks kämen verwaist an. Registriert nach den Exportern, sonst setzt der Azure-Monitor-Exporter seinen eigenen. Schlägt `OTEL_TRACES_SAMPLER`. |
| Baggage nie weiterreichen (`TraceContextOnlyPropagator`) | Baggage von Aufrufern kann personenbezogene Werte enthalten. Es braucht beide Registrierungen: als DI-Singleton, weil ASP.NET Core den Propagator für eingehende Requests aus DI holt, und als `DistributedContextPropagator.Current`, den HttpClient-Handler beim Erzeugen für ausgehende übernehmen. |
| `AddTelemetry()` überschreibt `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` passend zu `Telemetry:RecordSensitiveData`, in Prozessumgebung und Konfiguration | Aspire setzt die Variable lokal immer auf `true`. Microsoft.Extensions.AI liest sie einmalig aus der Prozessumgebung, andere Bibliotheken aus der Konfiguration. |
| `ActivityTrackingOptions.None` | Der OpenTelemetry-Logger hängt Trace- und Span-ID selbst an, die Standardoptionen des Generic Host ergänzten sie doppelt als Scope-Attribute. |
| Host außerhalb von `RunAsync()` selbst entsorgen | Nur das Entsorgen schreibt die gepufferte Telemetrie raus (höchstens 5 s pro Signal), `StopAsync` nicht. `RunAsync()` entsorgt erst, wenn es erreicht ist, sonst gingen etwa die Logs einer beim Start der API gescheiterten Migration verloren. Doppeltes Entsorgen ist harmlos. |
| Application Insights am Workspace der Container-Apps-Umgebung | Kein zweiter Workspace (ohne `WithLogAnalyticsWorkspace` legte Aspire einen an), die Namen von Umgebung und Workspace bleiben. |
| Live Metrics aus | Zusätzliche ausgehende Verbindung mit Live-Details zu Anfragen und Exceptions, für die Dev-API ohne Nutzen. |
| `EnableTraceBasedLogsSampler = false` | Der Standard verwürfe Logs nicht aufgezeichneter Traces. Die Sampling-Optionen des Exporters (`TracesPerSecond`, `SamplingRatio`) wirken nicht, unser Sampler ersetzt seinen. |
| `DisablePersistOnShutdown`, in `auth` `disableOfflineStorage` | Ungesendetes schriebe der Exporter beim Beenden auf die Platte, die im Job und in den auf null skalierenden Apps flüchtig ist. |
| AppHost setzt `OTEL_SERVICE_NAME` im Publish-Mode | Aspire setzt ihn nur im Run-Mode. |
| Konsole in Azure erst ab `Warning` (`Logging__Console__LogLevel__Default`) | Container Apps schreibt die Konsole in denselben Workspace, Logs kämen doppelt. Warnungen bleiben, damit Abstürze beim Start sichtbar sind. Die OpenTelemetry-Logs bleiben bei `Information`. |
| Tägliche Obergrenze des Workspaces | Schutz vor Kostenspitzen. Ist sie erreicht, nimmt der Workspace bis zum Folgetag nichts mehr auf, auch keine Konsolenlogs. Mit LLM-Aufrufen erreicht Dev sie schneller, weil `Telemetry:RecordSensitiveData` die Inhalte mitschreibt. Ohne `LogAnalytics:DailyQuotaGb` keine Obergrenze. |
| Konto-Id nur unter `UserId` | Ein Name wie in `UserCreated` und `UserDeleted`, ein Filter findet Requests, Logs und Exceptions. Verworfen: zusätzlich `enduser.id` (der Exporter macht daraus `user_AuthenticatedId`). |
| Konto-Id per Middleware mit Abfrage pro angemeldeter Anfrage | Das JWT trägt nur die Better-Auth-ID. Die Abfrage legt nie ein Konto an (das bleibt `GET /v1/users/me`) und nutzt nur `MurmliDbContext`, nicht `UserRegistry`, dessen Konstruktor den `AuthServiceClient` baut. |
| `UseExceptionHandler()` nach `UserTelemetryMiddleware` | Ohne Handler loggte Kestrel unbehandelte Exceptions ohne `UserId`. Folgen: 500 als Problem Details ([api.md](api.md#pipeline)), der Request-Span behält Status `Error`, `error.type` ist aber `500` statt des Exception-Typs. Ein Exception-Event am Span setzt ASP.NET Core 11 in keinem Fall. |
| `auth`: OpenTelemetry auf der SDK-2.x-Linie (`@opentelemetry/instrumentation-undici` 0.33.0 ist deren letzte Version) | `@azure/monitor-opentelemetry-exporter` unterstützt SDK 3.x noch nicht (sein Tag `latest` zeigt auf eine ältere Beta als die genutzte). Ausnahme von „neueste Version“, bis der Exporter SDK 3.x unterstützt. |
| `auth`: `fetch`-Spans über `@opentelemetry/instrumentation-undici`, Query per `startSpanHook` entfernt | Die HTTP-Instrumentierung sieht `fetch` nicht. Die undici-Instrumentierung schwärzt nichts, Application Insights übernimmt `url.full` wörtlich. Der Pfad bleibt. `traceparent` und `tracestate` gehen wie bei .NET an jedes Ziel, also auch an Apple und Google. |
| Perzentile des Rückstands aus Span-Attributen, Histogramme nur für Minimum und Maximum | Application Insights speichert von Histogrammen nur Summe, Anzahl, Minimum und Maximum. Verworfen: Buckets als eigene Counter (eine Zeitreihe je Bucket). |
| Keine Kind-Spans je Phase des Abgleichs | Die Npgsql-Spans zeigen die Zeiten der Datenbank; weitere Spans je Anfrage kosteten Volumen gegen die tägliche Obergrenze von Dev. |
| Werte von Metrik-Tags nur aus festen Mengen des Servers | Strings aus der Anfrage könnten Inhalte tragen und machten aus jedem Wert eine eigene Zeitreihe. Unbekannte Kommandoarten und Datentypen zählen als `unknown`. |
| Abgewiesene Anfragen zählt nur `/v1/sync` selbst | Die 503 der `UserTelemetryMiddleware` gilt für jeden Endpunkt und hat ihren Log `DatabaseUnavailable`; die 415 des Routings sowie 401 und 403 der Anmeldung erreichen den Endpunkt nie. Alle stehen mit ihrem Status in `requests`. |
| Kommandos der Beiträge zum ersten Abgleich nicht in `murmli.sync.commands` | Sie laufen in der Transaktion, die den persönlichen Bereich anlegt und die die Execution Strategy wiederholen kann, und liefern kein Ergebnis; den ersten Abgleich meldet `SyncAccountBootstrapped`. |
| `SyncCommandRejected` nur für Kommandos von Geräten | Ein Kommando des Servers liefert sein Ergebnis dem aufrufenden Code, `murmli.sync.commands` zählt es mit `server`. |
| `SyncScopeRemoved` höchstens 100 je Antwort | Das Token ist nicht signiert, ein gefälschtes kann bis zur Grenze des Bodys rund 26.700 unbekannte Bereiche nennen, die alle als entfernt zurückkommen. Ein Log je Bereich brächte die tägliche Obergrenze von Dev mit rund 15 Anfragen eines Kontos an ihr Ende. Ein echtes Token nennt höchstens 85 Bereiche, `murmli.sync.scopes.removed` zählt alle. |
| `murmli.sync.cleanup.last_success_time` nur im Speicher je Instanz | Der Alarm stützt sich auf `SyncCleanupCompleted`, das auch ohne laufende Instanz abfragbar ist. Der Wert ist die Uhr, mit der der Lauf begann (`TimeProvider`). |
| Tests mit Metriken ohne Parallelität, im Test Delta-Temporalität | Versuch vom 2026-11-23: Der `MeterProvider` eines Hosts zeichnete die Messwerte eines zweiten Hosts im selben Prozess mit auf, dessen Meter gleich hieß und aus dessen eigener `IMeterFactory` stammte. Nicht erneut als Filter je Host versuchen. |
