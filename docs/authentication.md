# Authentifizierung

Die App meldet Nutzer über Zitadel an. Die API prüft die Access Tokens (JWT) selbst anhand der veröffentlichten Schlüssel der Instanz, ohne Introspection. `AddApi()` registriert den JWT-Bearer-Handler und eine Fallback-Policy, die jeden Endpunkt ohne eigene Auth-Metadaten schützt, auch unbekannte Routen. Ohne Anmeldung erreichbar ist nur, was ausdrücklich `AllowAnonymous()` bekommt, derzeit `/health` und, wo eingeschaltet, das OpenAPI-Dokument (siehe [api.md](api.md#openapi-dokument)).

## Konfiguration

- Der AppHost liest `Zitadel:Authority` und `Zitadel:ProjectId` aus seiner `appsettings.<Umgebung>.json` und reicht sie per `WithZitadel()` als `Authentication__Schemes__Bearer__Authority` und `Authentication__Schemes__Bearer__ValidAudiences__0` an die API. Fehlt ein Wert, bricht der AppHost ab, etwa bei `aspire publish` ohne `--environment` (siehe [azure.md](azure.md)).
- `WithZitadel()` setzt außerdem `Zitadel__Authority` (derselbe Wert) und `Zitadel__ServiceUserToken` (der Secret-Parameter `zitadel-service-user-token`, siehe [azure.md](azure.md)) für den Lösch-Aufruf. `AddApi()` bindet beides als `ZitadelOptions` und prüft es beim Start, ohne Wert startet die API nicht.
- Zwei Authority-Einstellungen, weil `Authentication:Schemes:Bearer:Authority` die Token-Prüfung steuert und `Zitadel:Authority` die Basisadresse des Clients ist: Tests dürfen die erste nicht setzen (sonst lädt der Handler Metadaten), brauchen aber die zweite.
- Der Handler bindet die Sektion `Authentication:Schemes:Bearer` selbst. Mit gesetzter Authority lädt er die OIDC-Metadaten samt Schlüsseln beim ersten Request mit Token und cacht sie.
- Er akzeptiert nur Tokens, deren `aud` die Projekt-ID enthält. Tokens anderer Projekte derselben Instanz lehnt er ab.
- Dev nutzt lokal und in Azure eine Instanz in Zitadel Cloud (Region EU). Ihre Werte stehen in `appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost.

## Datenmodell

- `identity.users`: eigene, zufällige Id, Zitadel-ID (`sub`, unique) und Anlagezeitpunkt. Keine E-Mail, kein Name.
- `identity.deleted_users`: Löschvermerke mit Zitadel-ID (nicht eindeutig) und Löschzeitpunkt, siehe „Konto löschen“.
- `UserRegistry.GetOrCreateAsync` legt das Konto beim ersten Aufruf an, `GET /v1/users/me` liefert `{"id": "..."}`. Verhindert ein Löschvermerk die Anlage, antwortet es mit 401.
- Alle Nutzerdaten hängen an `identity.users.id`, nie an der Zitadel-ID. Ein Wechsel der Zitadel-Instanz berührt so nur die Zuordnung, nicht die Daten.

## Konto löschen

`DELETE /v1/users/me` ruft `UserRegistry.DeleteAsync` auf und antwortet mit 204, wenn alles gelöscht ist. Die Schritte laufen ohne Transaktion, jeder lässt sich wiederholen:

1. Die eigene Id des Kontos lesen, nur für den Log. Sie fehlt, wenn der Nutzer nie `GET /v1/users/me` aufgerufen hat.
2. Einen Löschvermerk (Zitadel-ID, Zeitpunkt) in `identity.deleted_users` schreiben.
3. Das Konto in `identity.users` löschen. Nutzerdaten in Postgres hängen per Fremdschlüssel mit `ON DELETE CASCADE` an `identity.users` und verschwinden mit. Daten außerhalb von Postgres (derzeit keine) muss `DeleteAsync` ausdrücklich vor dem Zitadel-Aufruf löschen.
4. Vermerke löschen, die älter als 24 Stunden sind.
5. Den Nutzer in Zitadel löschen (`DELETE {Zitadel:Authority}/v2/users/{Zitadel-ID}`, Timeout 10 s). 404 gilt als Erfolg, jeder andere Fehler führt zu 502.

Scheitert ein Schritt, kann sich der Nutzer weiter anmelden und die Löschung wiederholen, auch mit demselben Token. Der Endpunkt reicht `RequestAborted` nicht weiter (siehe „Entscheidungen“).

### Löschvermerk und `iat`

- Lokal geprüfte Tokens bleiben bis `exp` plus 5 Minuten Clock Skew von JwtBearer gültig, bei 1 Stunde Lebensdauer rund 65 Minuten. Ohne Vermerk legte `GET /v1/users/me` mit einem solchen Token nach der Löschung sofort ein neues Konto an.
- `GetOrCreateAsync` legt kein Konto an, wenn ein Vermerk für die Zitadel-ID existiert, der nicht vor dem `iat` des Tokens liegt. Fehlt `iat`, genügt irgendein Vermerk. Ein vorhandenes Konto liefert es immer.
- Ein neues Token nach einem gescheiterten Zitadel-Aufruf hat ein späteres `iat` und darf ein Konto anlegen, damit der Nutzer die Löschung wiederholen kann.
- Vermerke bleiben 24 Stunden, das liegt über den 12 Stunden, die Zitadel standardmäßig als Token-Lebensdauer vergibt. Aufgeräumt wird erst bei der nächsten Löschung.
- Restrisiko: Eine Kontoanlage parallel zu einer laufenden Löschung desselben Nutzers hinterlässt ein leeres Konto, das nur die Zitadel-ID trägt.

### Service-User

- Die API ruft Zitadel mit dem PAT eines Service-Users mit der Rolle `ORG_USER_MANAGER` auf. Der PAT hat ein Ablaufdatum und muss vorher erneuert werden.
- Antwort-Bodies von Zitadel loggt der Code nie, nur den Statuscode (`ZitadelUserDeletionFailed`).

## Logs und Telemetrie

- Der Nutzer erscheint nur mit der eigenen Id (`UserId`). `sub`, E-Mail und Token loggt der Code nie.
- Mit `Telemetry:RecordSensitiveData` (lokal und in Azure Dev) steht die Zitadel-ID als SQL-Parameterwert in den Command-Logs von EF Core.
- Die Zitadel-ID steht in der Adresse des Lösch-Aufrufs und damit in den Logs der HttpClientFactory und im HttpClient-Span. Das ist bewusst erlaubt.

## Tests

- `Murmli.Backend.Api.Tests` startet die API ohne Authority, mit eigenem Issuer, eigener Audience und einem zufälligen symmetrischen Schlüssel über `SigningKeys` (`TestTokens`). So läuft der echte Prüfpfad des Handlers, ohne Metadatenabruf.
- Kein Test spricht mit Zitadel. Ob echte Tokens der Instanz zur Konfiguration passen (JWT, Projekt in `aud`) und die Löschung in Zitadel wirkt, prüft ein Mensch nach dem Deploy.
- `ApiHost` ersetzt den Primary Handler aller HttpClients durch `ZitadelHttpMessageHandler` (zeichnet Anfragen auf, Statuscode einstellbar) und die Uhr durch einen `FakeTimeProvider`. `TestTokens.Create` setzt `iat` auf Wunsch, `exp` und `nbf` kommen weiter aus der echten Zeit, weil JwtBearer dafür die Systemuhr nutzt.
- Dass `/health` ohne Anmeldung erreichbar bleibt, deckt `HealthTests` in `Murmli.Backend.Aspire.Tests` ab.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| JWT lokal prüfen | Verworfen: Introspection, dann ginge jede Anfrage an Zitadel. Folge: Abmelden und Löschen wirken erst mit dem Ablauf des Tokens, gegen eine neue Kontoanlage nach dem Löschen hilft der Löschvermerk. |
| Konfiguration nur über die Bindung von `Authentication:Schemes:Bearer` | `AddJwtBearer` bindet die Sektion selbst und baut daraus `TokenValidationParameters`, inklusive `SigningKeys`. Ein im Code ersetztes `TokenValidationParameters` verwirft Issuer, Audiences und Schlüssel aus der Konfiguration. Der Delegate in `AddJwtBearer` läuft nach der Bindung. |
| `MapInboundClaims = false` | Sonst benennt der Handler `sub` in `ClaimTypes.NameIdentifier` um. Der Code liest `sub` direkt (`GetZitadelUserId()`). |
| Fallback-Policy mit `RequireClaim("sub")` | Schützt jeden Endpunkt ohne eigene Auth-Metadaten, auch künftige. `sub` ist damit in jedem geschützten Endpunkt vorhanden. Unter .NET 11 fügt `WebApplication` `UseAuthentication` und `UseAuthorization` selbst ein. |
| Get-or-create über den Unique-Index | EF Core 10 kann kein Upsert. Bei gleichzeitigen ersten Anfragen gewinnt ein Insert, die anderen scheitern mit Unique Violation, leeren den Change Tracker und lesen das Konto. Hat eine gleichzeitige Löschung es inzwischen entfernt, antworten sie mit 401. Die Unique Violation ist nicht transient, die Retry-Strategie der Aspire-Integration wiederholt sie nicht. EF Core loggt dabei für jede unterlegene Anfrage das gescheiterte Kommando und das gescheiterte Speichern als Error. Das ist erwartet und wird nicht unterdrückt. |
| `Guid.NewGuid()` statt Version 7 | Version 7 trüge den Anlagezeitpunkt in der Id, die später auch als pseudonyme Kennung gegenüber Drittanbietern dient. |
| Kein NuGet-Paket `Zitadel` | Gibt es nur für net8.0, es setzt auf Introspection. |
| Löschvermerk mit `iat` | Verworfen: bei der Kontoanlage Zitadel fragen, ob der Nutzer noch existiert, dann bräuchte jede Kontoanlage den PAT. |
| Zitadel als letzter Schritt der Löschung | Verworfen: Zitadel zuerst löschen. Scheitert danach ein Schritt, kann sich der Nutzer nicht mehr anmelden und die Löschung nicht wiederholen. |
| Mehrere Vermerke pro Nutzer | Verworfen: Upsert, EF Core 10 kann es nicht. Ein Insert reicht auch bei doppeltem Tippen und bei Wiederholungen. |
| Löschung ohne `RequestAborted` | Die App darf sich direkt nach dem Tippen schließen, eine begonnene Löschung soll trotzdem durchlaufen. |
