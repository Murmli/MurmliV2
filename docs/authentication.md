# Authentifizierung

Die App meldet Nutzer über Zitadel an. Die API prüft die Access Tokens (JWT) selbst anhand der veröffentlichten Schlüssel der Instanz, ohne Introspection. `AddApi()` registriert den JWT-Bearer-Handler und eine Fallback-Policy, die jeden Endpunkt ohne eigene Auth-Metadaten schützt, auch unbekannte Routen. Ohne Anmeldung erreichbar ist nur, was ausdrücklich `AllowAnonymous()` bekommt, derzeit `/health`.

## Konfiguration

- Der AppHost liest `Zitadel:Authority` und `Zitadel:ProjectId` aus seiner `appsettings.<Umgebung>.json` und reicht sie per `WithZitadel()` als `Authentication__Schemes__Bearer__Authority` und `Authentication__Schemes__Bearer__ValidAudiences__0` an die API. Fehlt ein Wert, bricht der AppHost ab, etwa bei `aspire publish` ohne `--environment` (siehe [azure.md](azure.md)).
- Der Handler bindet die Sektion `Authentication:Schemes:Bearer` selbst. Mit gesetzter Authority lädt er die OIDC-Metadaten samt Schlüsseln beim ersten Request mit Token und cacht sie.
- Er akzeptiert nur Tokens, deren `aud` die Projekt-ID enthält. Tokens anderer Projekte derselben Instanz lehnt er ab.
- Dev nutzt lokal und in Azure eine Instanz in Zitadel Cloud (Region EU). Ihre Werte stehen in `appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost.

## Datenmodell

- `identity.users`: eigene, zufällige Id, Zitadel-ID (`sub`, unique) und Anlagezeitpunkt. Keine E-Mail, kein Name.
- `UserRegistry.GetOrCreateAsync` legt das Konto beim ersten Aufruf an, `GET /users/me` liefert `{"id": "..."}`.
- Alle Nutzerdaten hängen an `identity.users.id`, nie an der Zitadel-ID. Ein Wechsel der Zitadel-Instanz berührt so nur die Zuordnung, nicht die Daten.

## Logs und Telemetrie

- Der Nutzer erscheint nur mit der eigenen Id (`UserId`). `sub`, E-Mail und Token loggt der Code nie.
- Mit `Telemetry:RecordSensitiveData` (lokal und in Azure Dev) steht die Zitadel-ID als SQL-Parameterwert in den Command-Logs von EF Core.

## Tests

- `Murmli.Backend.Api.Tests` startet die API ohne Authority, mit eigenem Issuer, eigener Audience und einem zufälligen symmetrischen Schlüssel über `SigningKeys` (`TestTokens`). So läuft der echte Prüfpfad des Handlers, ohne Metadatenabruf.
- Kein Test spricht mit Zitadel. Ob echte Tokens der Instanz zur Konfiguration passen (JWT, Projekt in `aud`), prüft ein Mensch nach dem Deploy.
- Dass `/health` ohne Anmeldung erreichbar bleibt, deckt `HealthTests` in `Murmli.Backend.Aspire.Tests` ab.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| JWT lokal prüfen | Verworfen: Introspection, dann ginge jede Anfrage an Zitadel. Folge: Abmelden und Löschen wirken erst mit dem Ablauf des Tokens. |
| Konfiguration nur über die Bindung von `Authentication:Schemes:Bearer` | `AddJwtBearer` bindet die Sektion selbst und baut daraus `TokenValidationParameters`, inklusive `SigningKeys`. Ein im Code ersetztes `TokenValidationParameters` verwirft Issuer, Audiences und Schlüssel aus der Konfiguration. Der Delegate in `AddJwtBearer` läuft nach der Bindung. |
| `MapInboundClaims = false` | Sonst benennt der Handler `sub` in `ClaimTypes.NameIdentifier` um. Der Code liest `sub` direkt (`GetZitadelUserId()`). |
| Fallback-Policy mit `RequireClaim("sub")` | Schützt jeden Endpunkt ohne eigene Auth-Metadaten, auch künftige. `sub` ist damit in jedem geschützten Endpunkt vorhanden. Unter .NET 11 fügt `WebApplication` `UseAuthentication` und `UseAuthorization` selbst ein. |
| Get-or-create über den Unique-Index | EF Core 10 kann kein Upsert. Bei gleichzeitigen ersten Anfragen gewinnt ein Insert, die anderen scheitern mit Unique Violation, leeren den Change Tracker und lesen das Konto. Die Unique Violation ist nicht transient, die Retry-Strategie der Aspire-Integration wiederholt sie nicht. EF Core loggt dabei für jede unterlegene Anfrage das gescheiterte Kommando und das gescheiterte Speichern als Error. Das ist erwartet und wird nicht unterdrückt. |
| `Guid.NewGuid()` statt Version 7 | Version 7 trüge den Anlagezeitpunkt in der Id, die später auch als pseudonyme Kennung gegenüber Drittanbietern dient. |
| Kein NuGet-Paket `Zitadel` | Gibt es nur für net8.0, es setzt auf Introspection. |
