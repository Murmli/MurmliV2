# API

Alle Endpunkte der API mappt `MapApi()` in `Murmli.Backend.Api`.

## Versionierung

Die App erreicht Nutzer über die Stores, alte Versionen bleiben noch lange in Gebrauch und sprechen weiter mit der API. Deshalb trägt der Pfad die Major-Version (etwa `/v1/users/me`), eine Version per Header oder Query gibt es nicht.

- `MapApi()` mappt alle Endpunkte in die Gruppe `/v1`. Ohne Version bleiben nur `/health` und das OpenAPI-Dokument.
- Pfade ohne Version wie `/users/me` sind unbekannte Routen: ohne Anmeldung antwortet die API mit 401 (die Fallback-Policy gilt auch für unbekannte Routen), mit Anmeldung mit 404.
- Bis zum ersten Live-Gang geht jede Änderung in `v1`, auch inkompatible. Danach ändert sich eine Version nur rückwärtskompatibel (neue Endpunkte, neue Felder in Antworten, Clients ignorieren unbekannte Felder), alles andere bekommt eine neue Version.

## OpenAPI-Dokument

Die API liefert unter `/openapi/v1.json` ein OpenAPI-3.2-Dokument, das alle Endpunkte mit HTTP-Methode (`MapGet`, `MapPost` usw.) unter `/v1` automatisch beschreibt, auch neue. `/health` steht nicht darin, Health-Check-Endpunkte tragen keine API-Beschreibung. Es dient derzeit dazu, einen Client-Generator für Flutter auszuprobieren.

- Eingeschaltet ist es nur lokal und in Azure Dev: `OpenApi:Enabled` steht in `appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost, `AddApi()` des AppHost reicht den Wert als `OpenApi__Enabled` an die API. Dort ist das Dokument ohne Anmeldung erreichbar.
- In jeder anderen Umgebung mappt die API die Route nicht: ohne Anmeldung antwortet sie mit 401 (die Fallback-Policy gilt auch für unbekannte Routen), mit Anmeldung mit 404.

## Produktsuche

`GET /v1/products?search=<Begriff>` sucht Produkte, die Regeln der Abfrage stehen in [database.md](database.md#produktsuche-und-extensions). Hat kein Wort des Begriffs mindestens 3 Zeichen, antwortet die API mit 400 als Validation Problem mit `errors.search`.

## Tests

- `ProductEndpointsTests` in `Murmli.Backend.Api.Tests` deckt die Produktsuche ab.
- `EndpointRouteBuilderExtensionsTests` in `Murmli.Backend.Api.Tests` deckt das OpenAPI-Dokument ab (erreichbar, wenn eingeschaltet, Version 3.2, nur Pfade unter `/v1`, 401 und 404, wenn ausgeschaltet) und dass Pfade ohne Version mit 404 antworten.
- `Api/ExtensionsTests` in `Murmli.Backend.Aspire.Tests` deckt das Weiterreichen des Schalters `OpenApi:Enabled` ab.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Präfix per `MapGroup` statt Asp.Versioning | Asp.Versioning hat noch kein Release für .NET 11, sein OpenAPI-Paket hängt an Microsoft.OpenApi 2.x, ASP.NET Core 11 braucht 3.x. Der Wechsel ist [#68](https://github.com/Murmli/MurmliV2/issues/68) und behält Pfade und Dokumentnamen. |
| Kein `WithGroupName("v1")` | Mit nur dem Dokument `v1` landen Endpunkte ohne Gruppennamen ohnehin darin. Mit einer zweiten Version setzt Asp.Versioning die Gruppennamen. |
| Schalter `OpenApi:Enabled` statt `IsDevelopment()` | Die API läuft in Azure Dev als Production, `Development` setzt nur `aspire run` über die launchSettings. `IsDevelopment()` schlösse Dev aus. |
| Validierungsfehler als `TypedResults.ValidationProblem` (400) | `HttpValidationProblemDetails` schreibt sich selbst, `AddProblemDetails` ist dafür nicht nötig. |
| Suchbegriff im Query-String | Suchbegriffe gelten nicht als Gesundheitsdaten, die Regel „Keine Gesundheitsdaten in URLs“ greift deshalb nicht. |
