# Telemetrie

`src/backend/Murmli.Backend.Telemetry` richtet OpenTelemetry für alle Backend-Hosts gemeinsam ein. Jeder Host ruft `builder.AddTelemetry()` nach dem Hinzufügen aller Konfigurationsquellen und vor `Build()` auf, weil es `Telemetry:RecordSensitiveData` beim Aufruf liest. Jede App-Ressource im AppHost bekommt `.WithTelemetry()`.

## Was aufgezeichnet wird

- Traces: eingehende Requests (`Microsoft.AspNetCore`), ausgehende HttpClient-Aufrufe (`System.Net.Http`), Npgsql-Zugriffe inklusive `COPY` und eigene Quellen `Murmli.*`.
- Metriken: ASP.NET Core (`Microsoft.AspNetCore.*`), HttpClient (`System.Net.Http`), DNS (`System.Net.NameResolution`), Runtime (`System.Runtime`), Npgsql und eigene Meter `Murmli.*`.
- Logs: alle `ILogger`-Einträge mit formatierter Nachricht und Scopes. Trace- und Span-ID hängen automatisch an.

Quelle und Meter `Npgsql` registrieren die Aspire-Integrationen selbst, `AddTelemetry()` ergänzt sie nicht.

## Export

- OTLP nur, wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist. Aspire setzt den Endpunkt im Run-Mode, lokal landet die Telemetrie damit im Dashboard. Ohne Endpunkt zeichnen die Apps auf, exportieren aber nichts.
- Export nach Azure: noch nicht gebaut, folgt mit [#24](https://github.com/Murmli/MurmliV2/issues/24).

## Sensible Daten

Der Schalter `Telemetry:RecordSensitiveData` (Standard aus) steuert alles, was Inhalte statt Struktur aufzeichnet. Code liest ihn nur über `IConfiguration.RecordsSensitiveData()`.

- LLM-Inhalte (Prompts und Antworten) über `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`.
- SQL-Parameterwerte in den Command-Logs von EF Core über `EnableSensitiveDataLogging`.

Npgsql-Spans enthalten nie Parameterwerte. Eingeschaltet ist der Schalter nur lokal: `appsettings.Development.json` des AppHost setzt ihn, `WithTelemetry()` reicht ihn als `Telemetry__RecordSensitiveData` an die Apps weiter. Für Azure Dev ist das Einschalten entschieden, aber noch nicht gebaut (`appsettings.azure-dev.json` setzt ihn nicht). Eingeschaltet wird er mit [#24](https://github.com/Murmli/MurmliV2/issues/24), dann entfällt dieser Hinweis.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Kein ServiceDefaults-Template | Es hängt an jeden HttpClient einen Resilience-Handler, der den Download des rund 13 GB großen Exports abbrechen würde, die API zum Entfernen ist experimentell. Außerdem filtert es `/health` aus den Traces. |
| Keine Instrumentation-Pakete | ASP.NET Core und HttpClient erzeugen unter .NET 11 selbst Activities nach den Semantic Conventions, gleichwertig zu den Paketen. Das ASP.NET-Core-Paket zöge außerdem ASP.NET Core in den Worker des Produktimports. |
| Sampler `AlwaysOn` | Jeder Request ist über seine Trace-ID auffindbar, auch wenn der Aufrufer das Sampled-Flag `00` schickt. Der Standard `ParentBased(AlwaysOn)` verwirft solche Requests. Der im Code gesetzte Sampler schlägt `OTEL_TRACES_SAMPLER`. |
| Baggage wird nie weitergereicht | Datenschutz: Baggage von Aufrufern kann personenbezogene Werte enthalten. `TraceContextOnlyPropagator` übernimmt nur `traceparent` und `tracestate`. Nur beide Registrierungen zusammen halten Baggage heraus: Als DI-Singleton verhindert er, dass ASP.NET Core eingehendes Baggage übernimmt, weil ASP.NET Core seinen Propagator aus DI holt. Als globaler `DistributedContextPropagator.Current`, den HttpClient-Handler beim Erzeugen übernehmen, verhindert er, dass Baggage aus eigenem Code ausgehend gesendet wird. |
| Schalter `Telemetry:RecordSensitiveData` | Standard aus, damit Inhalte nur bewusst in der Telemetrie landen. Aspire setzt `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` lokal immer auf `true`, deshalb überschreibt `AddTelemetry()` die Variable passend zum Schalter, in der Prozessumgebung und in der Konfiguration: Microsoft.Extensions.AI liest sie einmalig direkt aus der Prozessumgebung, andere Bibliotheken lesen die Konfiguration. |
| `ActivityTrackingOptions.None` | Der OpenTelemetry-Logger hängt Trace- und Span-ID selbst an, die Standardoptionen des Generic Host ergänzten sie doppelt als Scope-Attribute (`TraceId`, `SpanId`, `ParentId`). |
| `/health` bleibt in den Traces | Derzeit der einzige Request mit Datenbankzugriff. Filtern, sobald ein echter Endpunkt mit Datenbankzugriff existiert oder Container-Apps-Probes `/health` regelmäßig aufrufen. |
| Job-Hosts entsorgen den Host (`using var host`) | Erst das Entsorgen schreibt die gepufferte Telemetrie raus, `StopAsync` nicht. Das wartet pro Signal höchstens 5 s. |
