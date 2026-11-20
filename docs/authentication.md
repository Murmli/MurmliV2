# Authentifizierung

Better Auth läuft als eigener Node-Dienst `auth` (`src/auth`). Angemeldete Clients holen dort ein kurzlebiges JWT, die API prüft es lokal mit den von `auth` veröffentlichten Schlüsseln, ohne Introspection.

## Anmeldung

1. `POST /api/auth/sign-in/email` mit E-Mail und Passwort: Der Header `set-auth-token` trägt das signierte Session-Token (Plugin `bearer` mit `requireSignature: true`).
2. `GET /api/auth/token` mit `Authorization: Bearer <Session-Token>` liefert das JWT: ES256, 15 Minuten gültig, nur `sub` (Better-Auth-ID), `iss` (`BETTER_AUTH_URL`, eigener Endpunkt von `auth`), `aud` (`murmli-api`), `iat` und `exp`.
3. API-Aufrufe mit `Authorization: Bearer <JWT>`, vor Ablauf ein neues JWT über Schritt 2.
4. `POST /api/auth/sign-out`. Ohne Abmeldung gilt eine Session 7 Tage und verlängert sich bei Nutzung (Standard von Better Auth).

Entschieden, noch nicht gebaut: Die App übernimmt diesen Ablauf, sobald sie eine Anmeldung bekommt, dann entfällt dieser Absatz. Bis dahin nutzen ihn nur `AuthenticationTests` und lokale Clients.

## Konfiguration

- Lokal lauscht `auth` fest auf Port 5021 als stabile Adresse für lokale Clients (etwa den Flavor `local`, siehe [app.md](app.md#flavors)). In Azure läuft es mit 0 bis 1 Replica.
- `better-auth-secret` (`BETTER_AUTH_SECRET`) verschlüsselt die privaten Schlüssel in `jwks` und signiert die Session-Tokens. Ein neuer Wert machte die gespeicherten Schlüssel unlesbar und alle Sessions ungültig. Rotation ginge nur über `BETTER_AUTH_SECRETS` und bräuchte eine Änderung am AppHost.
- `auth-service-key` (`AUTH_SERVICE_KEY`) ist das einzige, was `POST /service/delete-user` am öffentlichen Ingress schützt. Er lässt sich jederzeit ersetzen, der neue Wert wirkt mit dem nächsten Deploy.
- `RequireHttpsMetadata=false` bekommt die API nur im Run-Mode, weil `auth` lokal nur http spricht.

## Dienst `auth`

- Node führt das TypeScript per Type Stripping ohne Build aus (`node src/main.ts`): nur Syntax, die nach bloßem Entfernen der Typen läuft (`erasableSyntaxOnly`), Importe mit Endung `.ts`.
- Die Node-Version steht nur in `src/auth/.nvmrc`, Aspire erzeugt daraus beim Publish das Dockerfile.
- In Azure meldet sich `auth` per Entra-Token als `backend-identity` an Postgres an (`PGUSER`, nur im Publish-Mode gesetzt).
- Beim Start migriert `auth` sein Schema (siehe [database.md](database.md#schemas)), legt im Run-Mode den Test-Nutzer an und lauscht erst danach. Scheitert das, beendet es sich mit Fehler.

### Rate Limiting

- Nur in Azure aktiv (`NODE_ENV=production`), im Speicher pro Replica, Client-IP aus `X-Forwarded-For`.
- Offen, bis ein Mensch es nach dem ersten Deploy geprüft hat, dann entfällt dieser Punkt: ob Container Apps an ein vom Client gesendetes `X-Forwarded-For` anhängt. Prüfung: einmal normal und einmal mit gefälschtem Header anmelden, die IP der Session muss jeweils die echte sein, im Log darf keine Warnung „Rate limiting could not determine a client IP“ stehen. Hängt Container Apps an, `advanced.ipAddress.trustedProxies` setzen.

## Nutzer anlegen

- Lokal: Im Run-Mode legt `auth` beim Start `test@murmli.de` mit Passwort `password` an, wenn er fehlt (Postgres läuft ohne Volume).
- Azure (nur ein Mensch): `az containerapp exec --name auth --resource-group rg-murmli-dev --command sh`, darin `npm run create-user -- <email> <name>` (fragt das Passwort ab).

## Datenmodell

- `identity.users` trägt nur eigene zufällige Id, `auth_user_id` (gleich `sub`, unique) und Anlagezeitpunkt, Name und E-Mail liegen nur bei Better Auth. Nutzerdaten hängen an der eigenen Id, damit ein Wechsel des Identity-Providers nur die Zuordnung berührt.
- `GET /v1/users/me` legt das Konto beim ersten Aufruf an, verhindert ein Löschvermerk (`identity.deleted_users`) das, antwortet es mit 401.

## Konto löschen

`DELETE /v1/users/me` antwortet mit 204, wenn alles gelöscht ist. Die Schritte laufen ohne gemeinsame Transaktion, jeder lässt sich wiederholen:

1. Löschvermerk (Better-Auth-ID, Zeitpunkt) schreiben.
2. Konto in `identity.users` löschen, Nutzerdaten folgen per `ON DELETE CASCADE`.
3. Vermerke löschen, die älter als 24 Stunden sind.
4. Nutzer in `auth` löschen per `POST /service/delete-user`, Timeout 10 s. Die ID steht im Body, damit sie weder in den Logs der HttpClientFactory noch im HttpClient-Span landet. Kein 2xx, Timeout oder Verbindungsfehler ergeben 502, geloggt wird nur Statuscode bzw. Exception (`AuthUserDeletionFailed`), nie der Antwort-Body. `auth` antwortet auch für unbekannte IDs mit 204.

Scheitert ein Schritt, kann sich der Nutzer weiter anmelden und die Löschung wiederholen, auch mit demselben Token.

### Löschvermerk und `iat`

- Lokal geprüfte JWTs gelten bis `exp` plus 5 Minuten Clock Skew von JwtBearer, also rund 20 Minuten. Ohne Vermerk legte `GET /v1/users/me` mit einem solchen Token nach der Löschung sofort ein neues Konto an.
- Kein neues Konto, wenn ein Vermerk nicht vor dem `iat` des Tokens liegt (ohne `iat` genügt jeder Vermerk), ein vorhandenes Konto wird immer geliefert. Ein nach einem gescheiterten Aufruf von `auth` geholtes JWT hat ein späteres `iat`, so lässt sich die Löschung wiederholen.
- Vermerke bleiben 24 Stunden (weit über diese rund 20 Minuten), aufgeräumt wird erst bei der nächsten Löschung.
- Restrisiko: Eine Kontoanlage parallel zur Löschung desselben Nutzers hinterlässt ein leeres Konto mit nur der Better-Auth-ID.

## Logs und Telemetrie

Konto-Id siehe [telemetry.md](telemetry.md#konto-id), `auth` siehe [telemetry.md](telemetry.md#auth). Akzeptierte Ausnahme: Mit `Telemetry:RecordSensitiveData` (nur lokal und in Azure Dev, dort nur Testdaten) steht die Better-Auth-ID als SQL-Parameterwert in den Command-Logs von EF Core, weil jede angemeldete Anfrage die Konto-Id abfragt.

## Tests

- `TestTokens` signieren mit einem zufälligen symmetrischen Schlüssel über `SigningKeys` statt `MetadataAddress`, so läuft der echte Prüfpfad ohne Metadatenabruf. `exp` und `nbf` kommen aus der echten Zeit, weil JwtBearer die Systemuhr nutzt, nicht den `FakeTimeProvider` von `ApiHost`.
- Die Span-Listener von `ApiHost` sind prozessweit: Die Liste enthält auch Spans parallel laufender Hosts und ist erst nach dem Entsorgen des Hosts vollständig. Tests werten sie danach aus und finden ihre Anfrage über eine eigene Trace-ID im `traceparent`.
- Die Tests einer Collection in `Murmli.Backend.Aspire.Tests` teilen sich eine `AppHostFixture`, damit Aspires npm-Installer nie parallel läuft.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| JWT lokal prüfen | Verworfen: Introspection per `get-session` (HTTP-Aufruf pro Anfrage, API fiele mit `auth` aus, doppelter Kaltstart bei Scale to Zero). Folge: Abmelden wirkt erst mit Ablauf des JWT (bis rund 20 Minuten). |
| Eigenes Discovery-Dokument in `auth` | Better Auths JWT-Plugin liefert nur `/api/auth/jwks`, JwtBearer lädt Schlüssel per Konfiguration nur über ein Discovery-Dokument. Verworfen: Plugin oauth-provider (macht Better Auth zum OAuth-Server), eigener `ConfigurationManager` (Code statt Konfiguration). |
| ES256 statt Better Auths Standard EdDSA | Microsoft.IdentityModel prüft kein EdDSA ([AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet#2426](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/2426), offen), OKP-Schlüssel fielen still weg. Gespeicherte Schlüssel behalten ihren Algorithmus. |
| Nur Bindung von `Authentication:Schemes:Bearer` | Ein im Code ersetztes `TokenValidationParameters` verwirft Issuer, Audiences und `SigningKeys` aus der Bindung. Der Delegate in `AddJwtBearer` läuft nach der Bindung. |
| Fallback-Policy mit `RequireClaim("sub")` | Gilt für jeden Endpunkt ohne eigene Auth-Metadaten, auch künftige. `GetAuthUserId()` verlässt sich darauf, dass `sub` vorhanden ist, und wirft sonst. |
| `MapInboundClaims = false` | Sonst wird `sub` zu `ClaimTypes.NameIdentifier`, der Code liest `sub` direkt. |
| API greift nie auf die Tabellen von Better Auth zu | Hooks und Plugin-Logik (etwa ein künftiger Widerruf der Apple-Tokens) laufen nur in Better Auths Code. Nur Konvention: Die gemeinsame Rolle von `backend-identity` könnte die Tabellen lesen, dass `api` und `product-import` so an Passwort-Hashes und Sessions kämen, ist ein akzeptiertes Risiko. |
| Get-or-create über den Unique-Index, mehrere Löschvermerke pro Nutzer | EF Core 10 kann kein Upsert. Bei gleichzeitigen ersten Anfragen gewinnt ein Insert, die übrigen lesen nach der Unique Violation das Konto. Die Retry-Strategie wiederholt sie nicht (nicht transient). Die Error-Logs von EF Core dazu sind erwartet und werden nicht unterdrückt. Doppelte Löschvermerke (doppeltes Tippen, Wiederholung) schaden nicht. |
| `Guid.NewGuid()` statt Version 7 | Version 7 trüge den Anlagezeitpunkt in der Id, die später auch als pseudonyme Kennung gegenüber Drittanbietern dient. |
| Löschvermerk mit `iat` | Verworfen: bei der Kontoanlage prüfen, ob der Nutzer in Better Auth noch existiert, die API hat keinen Lesezugriff auf Better Auth. |
| Löschen über `/service/delete-user` mit Service-Key | Verworfen: Better Auths `/delete-user` (`user.deleteUser`), braucht die Session des Nutzers und erlaubte Löschungen an der API vorbei. Verworfen: `remove-user` des Admin-Plugins, braucht einen Admin-Nutzer mit Passwort-Secret und öffnet Ban, Impersonation und Nutzerlisten am öffentlichen Ingress. |
| Better Auth als letzter Schritt der Löschung | Scheitert sonst ein späterer Schritt, kann sich der Nutzer nicht mehr anmelden und die Löschung nicht wiederholen. |
| Löschung und Anlage in `auth` über `runWithTransaction` (`@better-auth/core/context`, deshalb direkte Dependency) | `internalAdapter.deleteUser` löscht in getrennten Statements, ein Fehler hinterließe einen Nutzer ohne Anmeldemöglichkeit. Verworfen: `context.adapter.transaction`, der `internalAdapter` nimmt die Transaktion nur aus dem AsyncLocalStorage von `runWithTransaction`. Jeder pg-Client braucht einen `error`-Listener, sonst beendet ein Verbindungsabbruch während einer offenen Transaktion den Prozess. |
| Timeout von 10 s trotz Scale to Zero von `auth` | Akzeptiert: In Dev kann die erste Löschung nach einer Pause am Kaltstart scheitern (502, wiederholbar). Entschieden, noch nicht gebaut: In Produktion läuft `auth` mit mindestens einer Replica, die Notiz entfällt, sobald der AppHost das für eine Produktionsumgebung setzt. |
| Löschung ohne `RequestAborted` | Eine begonnene Löschung soll durchlaufen, auch wenn sich die App direkt schließt. Das gilt auch für den Abruf der Konto-Id in `UserTelemetryMiddleware` vor jedem Endpunkt, sonst begänne die Löschung nie (eine einzelne Index-Abfrage, ein Abbruch spart nichts). Verworfen: `RequestAborted` nur bei `DELETE` ignorieren, das müsste bei jedem neuen Endpunkt mitgedacht werden. |
| Telemetrie bleibt beim Löschen erhalten | Sie läuft mit der Aufbewahrungsfrist aus, die zufällige Id verliert ihre Zuordnung mit dem Konto. Verworfen: gezieltes Entfernen, unverhältnismäßig für pseudonyme Daten. |
| Nutzer in Azure per `npm run create-user`, Registrierung aus | Nur für Dev, deshalb ohne verdeckte Passworteingabe und ohne Prüfung der E-Mail. Entschieden, noch nicht gebaut: In Produktion gibt es nur die Anmeldung mit Google und Apple ohne Passwort-Nutzer, die Notiz entfällt mit dieser Anmeldung. Verworfen: Admin-Plugin, zeitweise offene Registrierung, lokales Skript gegen Postgres in Azure (die Firewall lässt nur Azure zu). |
| `--omit=dev` nur im Publish-Mode | Aspire 13.6.5 erzeugt für `AddNodeApp` keine eigene Stage für Produktions-Abhängigkeiten. Im Run-Mode entfernte Aspires Installer damit Biome und tsc aus `src/auth/node_modules`. Verworfen: eigenes Dockerfile in `src/auth` (schaltet die Generierung ab), `WithDockerfileBuilder` (experimentell). |
