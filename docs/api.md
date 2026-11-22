# API

Lokal lauscht die API über das Launch-Profil `http` fest auf Port 5020, damit lokale Clients eine stabile Adresse haben (etwa der Flavor `local`, siehe [app.md](app.md#flavors)).

## Pipeline

1. `UseRouting`, `UseAuthentication` und `UseAuthorization` fügt `WebApplication` selbst ein (im Environment `Development` davor die Developer Exception Page). Geschützte Endpunkte ohne Anmeldung enden hier mit 401.
2. `UserTelemetryMiddleware` (siehe [telemetry.md](telemetry.md#konto-id)). Scheitert der Abruf der Konto-Id an einer Störung der Datenbank (Retries erschöpft, transienter Npgsql-Fehler, Timeout), antwortet sie mit 503 als Problem Details mit `type` `tag:murmli.de,2026:service-unavailable` und einem `Retry-After` (zufällig zwischen `Database:UnavailableRetryAfter` und dem Doppelten, Standard 10 s) und loggt `DatabaseUnavailable` ohne `UserId`.
3. `UseExceptionHandler()` loggt unbehandelte Exceptions mit `UserId` und antwortet mit 500 als Problem Details (mit `traceId`, ohne Details der Exception). Ohne JSON im `Accept`-Header bleibt die 500-Antwort ohne Body.
4. Die Endpunkte.

Andere Exceptions vor Schritt 3 (Authentifizierung, Abruf der Konto-Id) behandelt Kestrel: 500 ohne Body, Log ohne `UserId`, lokal die Developer Exception Page.

## Versionierung

Der Pfad trägt die Major-Version (`/v1/...`), weil alte App-Versionen aus den Stores noch lange mit der API sprechen. Clients ignorieren unbekannte Felder, deshalb sind neue Felder rückwärtskompatibel. Pfade ohne Version sind unbekannte Routen: ohne Anmeldung 401 (die Fallback-Policy gilt auch für unbekannte Routen), mit Anmeldung 404.

## OpenAPI-Dokument

- `/openapi/v1.json` (OpenAPI 3.2) beschreibt automatisch alle Endpunkte unter `/v1`, `/health` nicht. Es dient derzeit dazu, einen Client-Generator für Flutter auszuprobieren.
- Eingeschaltet nur lokal und in Azure Dev (`OpenApi:Enabled`, vom AppHost als `OpenApi__Enabled` weitergereicht), dort ohne Anmeldung erreichbar. Sonst ist die Route nicht gemappt (401 bzw. 404 wie bei Pfaden ohne Version).

## Produktsuche

`GET /v1/products?search=<Begriff>`, Regeln der Abfrage siehe [database.md](database.md#produktsuche-und-extensions). Hat kein Wort mindestens 3 Zeichen, antwortet die API mit 400 (Validation Problem mit `errors.search`).

Höchstens 25 Treffer, jede Stufe entscheidet nur bei Gleichstand der vorigen ([#74](https://github.com/Murmli/MurmliV2/issues/74)):

1. Treffer über den Code, auch ohne Nährwerte.
2. Mit Kalorienwert vor ohne.
3. Mehr ausgefüllte Nährwerte (von 8) vor weniger.
4. Name beginnt mit dem Suchbegriff.
5. Höherer `popularity_key` vor niedrigerem, ohne gilt als niedrigster.
6. Name, dann Code.

Thekenartikel brauchen keine eigene Regel: Ihr Code wechselt mit jeder Packung und meist fehlen Nährwerte, sie rutschen ohnehin nach hinten.

## Problem-Typen

Fehler, auf die Clients eigens reagieren, sind Problem Details mit `type` `tag:murmli.de,2026:<code>`, geschrieben per `TypedResults.Problem` (auch ohne JSON im `Accept`-Header mit Body). Clients reagieren auf `type`, nie auf den bloßen Status, weil Proxys und das Framework dieselben Statuscodes erzeugen.

| Code | Status | Header | Quelle |
| --- | --- | --- | --- |
| `service-unavailable` | 503 | `Retry-After` | `UserTelemetryMiddleware`, wenn der Abruf der Konto-Id an einer Störung der Datenbank scheitert ([Pipeline](#pipeline)) |

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Präfix per `MapGroup` statt Asp.Versioning | Asp.Versioning hat noch kein Release für .NET 11, sein OpenAPI-Paket hängt an Microsoft.OpenApi 2.x, ASP.NET Core 11 braucht 3.x. Der Wechsel ist [#68](https://github.com/Murmli/MurmliV2/issues/68) und behält Pfade und Dokumentnamen. |
| Kein `WithGroupName("v1")` | Mit nur dem Dokument `v1` landen alle Endpunkte ohnehin darin, ab einer zweiten Version setzt Asp.Versioning die Gruppennamen. |
| Schalter `OpenApi:Enabled` statt `IsDevelopment()` | Die API läuft in Azure Dev als Production, `Development` setzt nur `aspire run` über die launchSettings. |
| Problem Details über `AddProblemDetails()` | `UseExceptionHandler()` ohne Pfad braucht den `IProblemDetailsService`. Über ihn schreiben auch `TypedResults.ValidationProblem` (400) und `TypedResults.Problem` (502 beim Löschen des Kontos, 503 bei einer Störung der Datenbank) samt `traceId`, ohne JSON im `Accept`-Header schreiben die beiden ihre Problem Details selbst. |
| Suchbegriff im Query-String | Suchbegriffe sind keine Gesundheitsdaten, „Keine Gesundheitsdaten in URLs“ greift nicht. |
| Problem-Typen als `tag:`-URI | `type` ist der Bezeichner aus RFC 9457, eine Tag-URI (RFC 4151) ist für Bezeichner ohne Seite dahinter gedacht. Verworfen: nur der Status, ein eigenes Feld `code`, `https:`-URIs ohne Seite. |
| `Retry-After` mit Zufall | Verteilt die Rückkehr wartender Geräte, damit sie nach einer Störung nicht alle im selben Moment wiederkommen. |
