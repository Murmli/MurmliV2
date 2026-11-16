# Telemetrie

`src/backend/Murmli.Backend.Telemetry` richtet OpenTelemetry für die .NET-Hosts (`api`, `product-import`) gemeinsam ein, `auth` hat ein eigenes Setup (siehe „`auth`“). Jeder .NET-Host ruft `builder.AddTelemetry()` nach dem Hinzufügen aller Konfigurationsquellen und vor `Build()` auf, weil es `Telemetry:RecordSensitiveData` beim Aufruf liest. Jede App-Ressource im AppHost bekommt `.WithTelemetry(applicationInsights)`.

## Was aufgezeichnet wird

Für die .NET-Hosts:

- Traces: eingehende Requests (`Microsoft.AspNetCore`), ausgehende HttpClient-Aufrufe (`System.Net.Http`), Npgsql-Zugriffe inklusive `COPY` und eigene Quellen `Murmli.*`. Aufrufe von `/health` werden samt ihrer Kind-Spans nicht aufgezeichnet.
- Metriken: ASP.NET Core (`Microsoft.AspNetCore.*`), HttpClient (`System.Net.Http`), DNS (`System.Net.NameResolution`), Runtime (`System.Runtime`), Npgsql und eigene Meter `Murmli.*`.
- Logs: alle `ILogger`-Einträge mit formatierter Nachricht und Scopes. Trace- und Span-ID hängen automatisch an.

Quelle und Meter `Npgsql` registrieren die Aspire-Integrationen selbst, `AddTelemetry()` ergänzt sie nicht.

## Export

- OTLP nur, wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist. Aspire setzt den Endpunkt im Run-Mode, lokal landet die Telemetrie damit im Dashboard.
- Azure Monitor nur, wenn `APPLICATIONINSIGHTS_CONNECTION_STRING` gesetzt ist. Das passiert nur in Azure: Im Publish-Mode legt der AppHost die Ressource `app-insights` an, `WithTelemetry(applicationInsights)` reicht den Connection String an die Apps weiter. Die Telemetrie landet damit in Application Insights am Log Analytics Workspace der Container-Apps-Umgebung.
- Ohne beides zeichnen die Apps auf, exportieren aber nichts.

## Sensible Daten

Der Schalter `Telemetry:RecordSensitiveData` (Standard aus) steuert alles, was Inhalte statt Struktur aufzeichnet. Code liest ihn nur über `IConfiguration.RecordsSensitiveData()`.

- LLM-Inhalte (Prompts und Antworten) über `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`.
- SQL-Parameterwerte in den Command-Logs von EF Core über `EnableSensitiveDataLogging`.

Npgsql-Spans enthalten nie Parameterwerte. Eingeschaltet ist der Schalter nur lokal und in Azure Dev, dort gibt es nur Testdaten: `appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost setzen ihn, `WithTelemetry()` reicht ihn als `Telemetry__RecordSensitiveData` an die Apps weiter. In jeder anderen Umgebung bleibt er aus.

## `auth`

`src/auth/src/telemetry.ts` startet das OpenTelemetry-NodeSDK.

- Nur Traces: eingehende HTTP-Requests, Abfragen über `pg` und die eigenen Spans von Better Auth (Scope `better-auth`). Keine OpenTelemetry-Metriken und -Logs. Better Auth loggt ab `warn` auf die Konsole, Container Apps schreibt die Konsole in den Workspace.
- `src/main.ts` registriert nur die Hooks von import-in-the-middle und importiert danach Telemetrie und Server dynamisch. `node:http` wird nur instrumentiert, wenn die Hooks vor seinem ersten Import registriert sind, `pg` patcht die Instrumentierung beim Laden.
- Export: im Run-Mode OTLP (gRPC) aus den Variablen, die Aspire setzt, in Azure `@azure/monitor-opentelemetry-exporter` mit `disableOfflineStorage`, ohne beides kein Export.
- Wie bei .NET: nur W3C Trace Context ohne Baggage, Sampler `ParentBased` mit `AlwaysOn` für Root und `remoteParentNotSampled`, `/health` nicht in den Traces (ohne Beachtung der Groß- und Kleinschreibung, mit abschließendem `/`), `pg`-Spans ohne Parameterwerte.
- Bei SIGTERM und SIGINT schreibt `auth` die gepufferten Spans raus, bevor es sich beendet. Node läuft im Container als PID 1 und beendet sich nur über diesen Handler.
- `auth` liest weder `Telemetry__RecordSensitiveData` noch `Logging__Console__LogLevel__Default`. Der AppHost reicht beides über `WithTelemetry()` trotzdem durch, das ist harmlos.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Kein ServiceDefaults-Template | Es hängt an jeden HttpClient einen Resilience-Handler, der den Download des rund 13 GB großen Exports abbrechen würde, die API zum Entfernen ist experimentell. |
| Keine Instrumentation-Pakete | ASP.NET Core und HttpClient erzeugen unter .NET 11 selbst Activities nach den Semantic Conventions, gleichwertig zu den Paketen. Das ASP.NET-Core-Paket zöge außerdem ASP.NET Core in den Worker des Produktimports. |
| Sampler `ParentBased` mit eigenem `HealthCheckExcludingSampler` für Root und entfernte Parents | Jeder Request außer `/health` ist über seine Trace-ID auffindbar, auch wenn der Aufrufer das Sampled-Flag `00` schickt, der Standard `ParentBased(AlwaysOn)` verwürfe solche Requests. `/health` wird verworfen, Kind-Spans folgen der Entscheidung ihres Parents. Ein `AlwaysOn`-Sampler ignorierte den Parent und zeichnete die Npgsql-Spans des Health-Checks verwaist auf. Registriert wird er nach den Exportern, weil der Azure-Monitor-Exporter sonst seinen eigenen Sampler setzt. Der im Code gesetzte Sampler schlägt `OTEL_TRACES_SAMPLER`. |
| Baggage wird nie weitergereicht | Datenschutz: Baggage von Aufrufern kann personenbezogene Werte enthalten. `TraceContextOnlyPropagator` übernimmt nur `traceparent` und `tracestate`. Nur beide Registrierungen zusammen halten Baggage heraus: Als DI-Singleton verhindert er, dass ASP.NET Core eingehendes Baggage übernimmt, weil ASP.NET Core seinen Propagator aus DI holt. Als globaler `DistributedContextPropagator.Current`, den HttpClient-Handler beim Erzeugen übernehmen, verhindert er, dass Baggage aus eigenem Code ausgehend gesendet wird. |
| Schalter `Telemetry:RecordSensitiveData` | Standard aus, damit Inhalte nur bewusst in der Telemetrie landen. Aspire setzt `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` lokal immer auf `true`, deshalb überschreibt `AddTelemetry()` die Variable passend zum Schalter, in der Prozessumgebung und in der Konfiguration: Microsoft.Extensions.AI liest sie einmalig direkt aus der Prozessumgebung, andere Bibliotheken lesen die Konfiguration. |
| `ActivityTrackingOptions.None` | Der OpenTelemetry-Logger hängt Trace- und Span-ID selbst an, die Standardoptionen des Generic Host ergänzten sie doppelt als Scope-Attribute (`TraceId`, `SpanId`, `ParentId`). |
| `/health` nicht in den Traces | Seit `GET /v1/users/me` gibt es echte Endpunkte mit Datenbankzugriff, Health-Traces wären nur Rauschen, lokal pollt Aspire alle 30 s. Der Pfad wird wie beim Routing ohne Beachtung der Groß- und Kleinschreibung und mit abschließendem `/` verglichen. |
| Hosts, die außerhalb von `RunAsync()` arbeiten, entsorgen den Host selbst (`using var host` bzw. `await using var app`) | Erst das Entsorgen schreibt die gepufferte Telemetrie raus, `StopAsync` nicht. Das wartet pro Signal höchstens 5 s. `RunAsync()` entsorgt den Host erst, wenn es erreicht ist: Scheitert vorher etwas, etwa die Migration beim Start der API, gingen die Logs dazu sonst verloren. Das zweite Entsorgen nach `RunAsync()` ist harmlos. |
| Application Insights am Workspace der Container-Apps-Umgebung | Kein zweiter Workspace, die Namen der Umgebung und des Workspaces bleiben gleich. Ohne `WithLogAnalyticsWorkspace` legte Aspire einen zweiten Workspace an. Die Umgebung legt ihren Workspace selbst an, für ihre Bicep-Ausgabe `AZURE_LOG_ANALYTICS_WORKSPACE_ID` hat Aspire keinen öffentlichen Zugriff, deshalb steht der Name als String im Code. |
| Application Insights nur im Publish-Mode | Im Run-Mode legte Aspire die Ressource in Azure an. `aspire run` und `dotnet test` kommen ohne Azure aus, lokal bleibt es beim Dashboard. |
| Exporter `Azure.Monitor.OpenTelemetry.Exporter` statt der Distro | Die Distro zieht die Instrumentierung von ASP.NET Core und SqlClient in den Worker des Produktimports und schaltet das Schwärzen von Query-Parametern in URLs ab. |
| Live Metrics aus | Zusätzliche ausgehende Verbindung, die Details zu Anfragen und Exceptions live überträgt. Für eine auf null skalierende Dev-API ohne Nutzen, gespeichert werden die Daten ohnehin nicht. |
| `EnableTraceBasedLogsSampler = false` im Azure-Monitor-Exporter | Logs nicht aufgezeichneter Traces bleiben erhalten, der Standard verwürfe sie. Die Sampling-Optionen des Exporters (`TracesPerSecond`, `SamplingRatio`) entfallen, weil unser Sampler seinen ersetzt. Ein Test prüft am fehlenden Tag `microsoft.sample_rate`, dass unser Sampler gewinnt. |
| `DisablePersistOnShutdown` | Was beim Beenden noch nicht gesendet ist, schriebe der Exporter sonst auf die Platte. Die ist im Job und in der auf null skalierenden API flüchtig, die Daten wären trotzdem verloren. Das Beenden wartet pro Signal höchstens 5 s auf den Versand. |
| `OTEL_SERVICE_NAME` setzt der AppHost im Publish-Mode | Aspire setzt den Dienstnamen nur im Run-Mode, in Azure fehlte er sonst. Der Wert ist wie lokal der Ressourcenname (`api`, `auth`, `product-import`). |
| Konsole in Azure nur ab Warnung (`Logging__Console__LogLevel__Default=Warning`) | Container Apps schreibt die Konsole in denselben Workspace, Logs kämen sonst doppelt an. Warnungen und Fehler bleiben, damit Abstürze beim Start sichtbar sind. Der Schalter gilt nur für die Konsole, die OpenTelemetry-Logs bleiben bei `Information`. `auth` liest den Schalter nicht, Better Auth loggt ohnehin erst ab `warn`. |
| Tägliche Obergrenze `LogAnalytics:DailyQuotaGb` pro Umgebung, in Dev 0,2 GB | Schutz vor Kostenspitzen. Ist sie erreicht, nimmt der Workspace bis zum Folgetag nichts mehr auf, auch keine Konsolenlogs. Sobald es LLM-Aufrufe gibt, erreicht Dev die Grenze schneller, weil der Schalter `Telemetry:RecordSensitiveData` dort die LLM-Inhalte mitschreibt. Ohne Wert bleibt der Workspace ohne Obergrenze. |
| `auth`: Exporter `@azure/monitor-opentelemetry-exporter` statt der Distro | Wie bei .NET nur der Exporter: Instrumentierung, Sampler und Propagator legt `telemetry.ts` selbst fest, die Distro brächte eigene mit. |
| `auth`: `disableOfflineStorage` | Wie `DisablePersistOnShutdown` bei .NET: Die Platte der auf null skalierenden Container App ist flüchtig. |
| `auth`: OpenTelemetry-Pakete auf der SDK-2.x-Linie (`@opentelemetry/sdk-node` 0.223.2, `@opentelemetry/api` 1.9.1) | `@azure/monitor-opentelemetry-exporter` (1.0.0-beta.47, das Tag `latest` zeigt auf eine ältere Beta) unterstützt SDK 3.x noch nicht. Bewusste Ausnahme von der Regel „neueste Version“, sie endet, sobald der Exporter SDK 3.x unterstützt. |
