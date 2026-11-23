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
- Der Aufräum-Lauf des Abgleichs, er läuft ohne Anfrage. Ausgenommen ist das Abschließen hängengebliebener Kontolöschungen: Es loggt je Konto mit `UserId` als Log-Scope ([authentication.md](authentication.md#konto-löschen)).

### Abgleich

`Murmli.Backend.Sync` loggt `SyncCommandFailed` (3002, Error mit Exception; `DeviceId`, `CommandId`, `DataType`, `CommandKind`), wenn ein einzelnes Kommando scheitert, und `SyncDatabaseUnavailable` (3003, Warning mit Exception; `Phase`: `bootstrap`, `commands` oder `changes`), wenn eine Störung der Datenbank die Anfrage mit 503 beendet. Beide tragen `UserId` aus dem Scope der `UserTelemetryMiddleware`. Inhalte von Datensätzen, Tokens und den Body loggt der Kern nie, `DataType` und `CommandKind` nur, wenn der Server sie kennt. Endet ein Schreiben des Servers mit `failed` ([sync.md](sync.md#server-als-schreiber)), loggt `SyncServerWriter` dasselbe `SyncCommandFailed` ohne `DeviceId`, mit `UserId` nur innerhalb einer Anfrage an die API.

Der Aufräum-Lauf ([sync.md](sync.md#aufbewahrung-und-aufräumen)) loggt ohne `UserId`: `SyncCleanupCompleted` (3005, Information; `DurationMilliseconds`, `RemovedTombstoneCount`, `RemovedProcessedCommandCount`, `RemovedRecordCount`) am Ende eines vollständigen Laufs, `SyncCleanupStopped` (3008, Warning; dieselben Properties), wenn er an seiner Zeitgrenze endet, `SyncCleanupSkipped` (3006, Information), wenn ein anderer Lauf die Lease hält, und `SyncCleanupFailed` (3007, Error mit Exception), wenn ein Beitrag oder der ganze Lauf scheitert, sein Aufrufer ihn abbricht oder die Lease nicht freigegeben werden kann (dann auch nach `SyncCleanupCompleted` oder `SyncCleanupStopped`; die Lease läuft nach `Sync:Cleanup:LeaseDuration` ab).

## Export

OTLP nur mit `OTEL_EXPORTER_OTLP_ENDPOINT` (setzt Aspire im Run-Mode), Azure Monitor nur mit `APPLICATIONINSIGHTS_CONNECTION_STRING` (nur in Azure, über `WithTelemetry(applicationInsights)`). Ohne beides wird aufgezeichnet, aber nicht exportiert.

## Sensible Daten

`Telemetry:RecordSensitiveData` (Standard aus) schaltet LLM-Inhalte (`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`) und SQL-Parameterwerte in den EF-Core-Command-Logs (`EnableSensitiveDataLogging`) ein. An ist er nur lokal und in Azure Dev, wo es nur Testdaten gibt (`appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost). Ausnahme für Dev ([#138](https://github.com/Murmli/MurmliV2/issues/138)): Der Sync-Kern schreibt Datensätze über EF Core, mit dem Schalter tragen die EF-Core-Command-Logs deshalb auch Inhalte abgeglichener Datensätze als SQL-Parameterwerte, in Azure Dev bis in Application Insights. Eigener Code loggt nie Inhalte. Ohne den Schalter (Standard und jede andere Umgebung) loggt EF Core keine Parameterwerte, und Npgsql-Spans tragen nur das SQL mit Platzhaltern.

## `auth`

- Nur Traces (HTTP ein und aus, `fetch`, `pg`, Spans von Better Auth mit Scope `better-auth`), keine OpenTelemetry-Metriken und -Logs. Better Auth loggt ab `warn` auf die Konsole, Container Apps schreibt sie in den Workspace.
- Ausgehende Spans über `node:http`/`node:https` tragen die URL samt Query, geschwärzt werden nur Zugangsdaten und einige Signatur-Parameter (etwa `sig`, `X-Amz-Signature`). In Azure steht so die `client_id` von `backend-identity` im Span des Token-Abrufs von `@azure/identity`.
- Eingehende Spans tragen die Query mit derselben Einschränkung in `url.query` (bei einem OAuth-Callback `code` und `state`). Der Azure-Monitor-Exporter übernimmt sie nicht, im lokalen Dashboard steht sie.
- `fetch`-Spans: URL ohne Query, von den Headern nur `User-Agent`, nie der Body. Über `fetch` ruft Better Auth Anmeldeanbieter auf, derzeit bei jeder Anmeldung mit Apple bzw. Google deren Schlüssel.
- `src/auth/src/main.ts` registriert nur die Hooks von import-in-the-middle und importiert Telemetrie und Server danach dynamisch: `node:http` wird nur instrumentiert, wenn die Hooks vor seinem ersten Import registriert sind, die Instrumentierung patcht `pg` beim Laden. `fetch` braucht keine Hooks (`diagnostics_channel`).
- Export, Propagator (W3C Trace Context ohne Baggage), Sampler und `/health`-Filter wie bei .NET, `pg`-Spans ohne Parameterwerte.
- Node läuft im Container als PID 1 und beendet sich bei SIGTERM und SIGINT nur über den eigenen Handler, der vorher die gepufferten Spans schreibt.
- `Telemetry__RecordSensitiveData` und `Logging__Console__LogLevel__Default` reicht der AppHost auch an `auth`, das sie nicht liest (harmlos).

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
