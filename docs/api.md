# API

Lokal lauscht die API über das Launch-Profil `http` fest auf Port 5020, damit lokale Clients eine stabile Adresse haben (etwa der Flavor `local`, siehe [app.md](app.md#flavors)).

## Pipeline

1. `UseRouting`, `UseAuthentication` und `UseAuthorization` fügt `WebApplication` selbst ein (im Environment `Development` davor die Developer Exception Page). Geschützte Endpunkte ohne Anmeldung enden hier mit 401.
2. `UserTelemetryMiddleware` (siehe [telemetry.md](telemetry.md#konto-id)). Scheitert der Abruf der Konto-Id an einer Störung der Datenbank (Retries erschöpft, transienter Npgsql-Fehler, Timeout), antwortet sie mit 503 als Problem Details mit `type` `tag:murmli.de,2026:service-unavailable` und einem `Retry-After` (zufällig zwischen `Database:UnavailableRetryAfter` und dem Doppelten, Standard 10 s) und loggt `DatabaseUnavailable` ohne `UserId`. Gibt es ein Konto, legt sie außerdem das `AccountFeature` (Konto-Id) an die Anfrage; `/v1/sync` liest das Konto nur daraus.
3. `UseExceptionHandler()` loggt unbehandelte Exceptions mit `UserId` und antwortet mit 500 als Problem Details (mit `traceId`, ohne Details der Exception). Ohne JSON im `Accept`-Header bleibt die 500-Antwort ohne Body.
4. Die Endpunkte.

Andere Exceptions vor Schritt 3 (Authentifizierung, Abruf der Konto-Id) behandelt Kestrel: 500 ohne Body, Log ohne `UserId`, lokal die Developer Exception Page.

## Versionierung

Der Pfad trägt die Major-Version (`/v1/...`), weil alte App-Versionen aus den Stores noch lange mit der API sprechen. Clients ignorieren unbekannte Felder, deshalb sind neue Felder rückwärtskompatibel. Pfade ohne Version sind unbekannte Routen: ohne Anmeldung 401 (die Fallback-Policy gilt auch für unbekannte Routen), mit Anmeldung 404.

## OpenAPI-Dokument

- `/openapi/v1.json` (OpenAPI 3.2) beschreibt automatisch alle Endpunkte unter `/v1`, `/health` nicht. Es dient derzeit dazu, einen Client-Generator für Flutter auszuprobieren.
- Eingeschaltet nur lokal und in Azure Dev (`OpenApi:Enabled`, vom AppHost als `OpenApi__Enabled` weitergereicht), dort ohne Anmeldung erreichbar. Sonst ist die Route nicht gemappt (401 bzw. 404 wie bei Pfaden ohne Version).
- `/v1/sync` deklariert Anfrage, Antwort und Problem-Typen ausdrücklich (`Accepts`, `Produces`, `ProducesProblem`), weil der Endpunkt den Body selbst liest und ein eigenes `IResult` zurückgibt; Kommandos und `fields` sind darin freie Objekte, die Felder je Datentyp stehen in [sync.md](sync.md).

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

## Abgleich

`POST /v1/sync` ([sync.md](sync.md)). Den Body liest der Endpunkt selbst, ohne Parameter-Binding, damit die Prüfungen in der Reihenfolge aus [sync.md](sync.md#fehler-der-ganzen-anfrage) laufen: Konto aus dem `AccountFeature` (sonst 410), fehlender `Content-Type` (415), `Murmli-Device-Id` (400), Body mit einer Kopie von `JsonSerializerOptions.Web` mit `NumberHandling = JsonNumberHandling.Strict`, damit eine Zahl in einem String als falscher Typ gilt, und `PropertyNameCaseInsensitive = false`, damit ein Feld nur mit exaktem Namen zählt und etwa `DataTypes` als unbekanntes Feld wegfällt (über `Sync:MaxRequestBytes` 413, kein JSON oder falscher Rahmen 400), Zahl der Kommandos (413). Einen vorhandenen `Content-Type`, der kein JSON ist, beantwortet schon das Routing mit 415 ohne Body (Metadaten von `Accepts<SyncRequest>`). Die Grenze für den Body setzt `RequestSizeLimitAttribute` als Metadaten des Endpunkts aus `Sync:MaxRequestBytes`, Kestrel bricht das Lesen darüber mit 413 ab. Kommandos bleiben `JsonElement`, der Kern prüft jedes einzeln. Die Verarbeitung bekommt ein mit `RequestAborted` verknüpftes Token mit der Frist `Sync:RequestDeadline` (45 s, Echtzeit); läuft sie ab, antwortet der Endpunkt mit 503. `Accept-Encoding` liest der Endpunkt mit `StringWithQualityHeaderValue.TryParseStrictList` (`Microsoft.Net.Http.Headers`) und gibt dem Kern als `SyncResponseEncoding` mit, ob das Gerät gzip annimmt. Die Antwort schreibt der Kern in einen Puffer, komprimiert nach [sync.md](sync.md#kompression), und nennt ihre Kodierung im Ergebnis; `SyncResponseResult` sendet ihn mit `Content-Type: application/json; charset=utf-8`, `Vary: Accept-Encoding`, `Content-Length` und bei gzip `Content-Encoding: gzip`.

## Problem-Typen

Fehler, auf die Clients eigens reagieren, sind Problem Details mit `type` `tag:murmli.de,2026:<code>`, geschrieben per `TypedResults.Problem` (auch ohne JSON im `Accept`-Header mit Body). Clients reagieren auf `type`, nie auf den bloßen Status, weil Proxys und das Framework dieselben Statuscodes erzeugen.

| Code | Status | Header | Quelle |
| --- | --- | --- | --- |
| `service-unavailable` | 503 | `Retry-After` | `UserTelemetryMiddleware`, wenn der Abruf der Konto-Id an einer Störung der Datenbank scheitert ([Pipeline](#pipeline)); `/v1/sync`, wenn der Kern an einer Störung der Datenbank scheitert oder die Frist der Anfrage abläuft |
| `account-gone` | 410 | – | `/v1/sync`: kein Konto zu `sub`, oder das Konto wurde während der Anfrage gelöscht |
| `request-too-large` | 413 | – | `/v1/sync`: Body über `Sync:MaxRequestBytes` oder mehr Kommandos als `Sync:MaxCommandsPerRequest`; mit den Feldern `maxRequestBytes` und `maxCommandsPerRequest` |
| `invalid-request` | 400, 415 | – | `/v1/sync`: Geräte-ID fehlt oder ist keine UUID v4, Body kein JSON oder Rahmen falsch (400); `Content-Type` fehlt (415) |

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Präfix per `MapGroup` statt Asp.Versioning | Asp.Versioning hat noch kein Release für .NET 11, sein OpenAPI-Paket hängt an Microsoft.OpenApi 2.x, ASP.NET Core 11 braucht 3.x. Der Wechsel ist [#68](https://github.com/Murmli/MurmliV2/issues/68) und behält Pfade und Dokumentnamen. |
| Kein `WithGroupName("v1")` | Mit nur dem Dokument `v1` landen alle Endpunkte ohnehin darin, ab einer zweiten Version setzt Asp.Versioning die Gruppennamen. |
| Schalter `OpenApi:Enabled` statt `IsDevelopment()` | Die API läuft in Azure Dev als Production, `Development` setzt nur `aspire run` über die launchSettings. |
| Problem Details über `AddProblemDetails()` | `UseExceptionHandler()` ohne Pfad braucht den `IProblemDetailsService`. Über ihn schreiben auch `TypedResults.ValidationProblem` (400) und `TypedResults.Problem` (502 beim Löschen des Kontos, 503 bei einer Störung der Datenbank, 400, 410, 413 und 415 von `/v1/sync`) samt `traceId`, ohne JSON im `Accept`-Header schreiben die beiden ihre Problem Details selbst. |
| Suchbegriff im Query-String | Suchbegriffe sind keine Gesundheitsdaten, „Keine Gesundheitsdaten in URLs“ greift nicht. |
| Problem-Typen als `tag:`-URI | `type` ist der Bezeichner aus RFC 9457, eine Tag-URI (RFC 4151) ist für Bezeichner ohne Seite dahinter gedacht. Verworfen: nur der Status, ein eigenes Feld `code`, `https:`-URIs ohne Seite. |
| `Retry-After` mit Zufall | Verteilt die Rückkehr wartender Geräte, damit sie nach einer Störung nicht alle im selben Moment wiederkommen. |
| Body von `/v1/sync` ohne Parameter-Binding | Das Binding liefe vor der Prüfung von Konto und Geräte-ID, und ein strenges Lesen (`JsonSerializerOptions.Strict`, doppelte Felder) machte aus einem fehlerhaften Kommando die 400 der ganzen Anfrage, die die App endlos wiederholt. |
| `[FromServices]` an den Diensten von `/v1/sync` | Ohne Binding-Quelle hielte Minimal API einen nicht registrierten Typ für den Body. |
| `Accept-Encoding` per `StringWithQualityHeaderValue.TryParseStrictList` | Ein Header mit einem fehlerhaften Eintrag gilt als nicht lesbar, die Antwort bleibt unkomprimiert ([sync.md](sync.md#kompression)). Verworfen: `GetTypedHeaders().AcceptEncoding` (liest nachsichtig, macht etwa aus `gzip;q=2` einen Eintrag `2` und wertet `gzip, br;q=bogus` als gzip). |
