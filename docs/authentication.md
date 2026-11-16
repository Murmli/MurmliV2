# Authentifizierung

Better Auth (npm-Paket `better-auth`) läuft als eigener Node-Dienst `auth` in `src/auth`. Wer sich dort anmeldet, holt sich ein kurzlebiges JWT, die API prüft es selbst anhand der von `auth` veröffentlichten Schlüssel, ohne Introspection. `AddApi()` registriert den JWT-Bearer-Handler und eine Fallback-Policy, die jeden Endpunkt ohne eigene Auth-Metadaten schützt, auch unbekannte Routen. Ohne Anmeldung erreichbar ist nur, was ausdrücklich `AllowAnonymous()` bekommt, derzeit `/health` und, wo eingeschaltet, das OpenAPI-Dokument (siehe [api.md](api.md#openapi-dokument)).

Die App spricht mit Better Auth nur zum Anmelden, Abmelden und Holen bzw. Erneuern des JWT, alles andere läuft über die API. Die API greift nie auf die Tabellen von Better Auth zu, sie nutzt immer die HTTP-API von `auth`.

## Anmeldung

1. `POST /api/auth/sign-in/email` mit E-Mail und Passwort. Die Antwort trägt im Header `set-auth-token` das signierte Session-Token (Plugin `bearer` mit `requireSignature: true`, unsignierte Tokens lehnt `auth` ab).
2. `GET /api/auth/token` mit `Authorization: Bearer <Session-Token>` liefert das JWT: ES256, 15 Minuten gültig, nur die Claims `sub` (Better-Auth-ID), `iss` (öffentliche URL von `auth`), `aud` (`murmli-api`), `iat` und `exp`. E-Mail und Name stehen nicht darin.
3. Die API wird mit `Authorization: Bearer <JWT>` aufgerufen, vor Ablauf holt der Client über Schritt 2 ein neues JWT.
4. `POST /api/auth/sign-out` beendet die Session. Ohne Abmeldung gilt eine Session nach Better Auths Standard 7 Tage und verlängert sich bei Nutzung.

Die App meldet noch niemanden an, den Ablauf nutzen derzeit `AuthenticationTests` und lokale Clients. Entschieden ist, dass die App ihn übernimmt, sobald sie eine Anmeldung bekommt, dann entfällt dieser Absatz.

## Konfiguration

- `AddAuth()` in `Auth/Extensions.cs` des AppHost startet `src/auth` als Node-App mit externem HTTP-Ingress, in Azure mit 0 bis 1 Replica. Lokal lauscht `auth` fest auf Port 5021, damit lokale Clients (etwa Bruno, später das Flavor `local` der App) eine stabile Adresse haben.
- `AddAuth()` setzt `BETTER_AUTH_URL` auf den eigenen HTTP-Endpunkt (daraus werden `iss` und die Adressen im Discovery-Dokument), `JWT_AUDIENCE` auf `murmli-api` sowie `BETTER_AUTH_SECRET` und `AUTH_SERVICE_KEY` aus den Secret-Parametern `better-auth-secret` und `auth-service-key` (Herkunft der Werte siehe [azure.md](azure.md#secret-parameter)).
- `better-auth-secret` verschlüsselt die privaten Schlüssel in `jwks` und signiert die Session-Tokens. Der Wert wird nie geändert, ein neuer machte die gespeicherten Schlüssel unlesbar und alle Sessions ungültig. Rotation ginge nur über `BETTER_AUTH_SECRETS` und bräuchte eine Änderung am AppHost.
- `WithAuth()` reicht der API `Authentication__Schemes__Bearer__MetadataAddress` (`<auth>/.well-known/openid-configuration`), `Authentication__Schemes__Bearer__ValidIssuer` (der Endpunkt von `auth`), `Authentication__Schemes__Bearer__ValidAudiences__0` (`murmli-api`), `AuthService__BaseAddress` und `AuthService__Key`. Nur im Run-Mode setzt es zusätzlich `Authentication__Schemes__Bearer__RequireHttpsMetadata=false`, weil `auth` lokal nur http spricht. Die API wartet auf `auth`.
- `AddApi()` bindet `AuthService` als `AuthServiceOptions` und prüft es beim Start, ohne Werte startet die API nicht.
- Der Handler bindet die Sektion `Authentication:Schemes:Bearer` selbst. Mit `MetadataAddress` lädt er beim ersten Request mit Token das Discovery-Dokument samt Schlüsseln und cacht sie. Trägt ein Token einen unbekannten `kid`, lädt er die Metadaten neu (Standard von JwtBearer).
- Er akzeptiert nur Tokens mit `iss` gleich dem Endpunkt von `auth` und `aud` gleich `murmli-api`.

## Dienst `auth`

- TypeScript in `src/auth/src`, Node führt es per Type Stripping direkt aus (`node src/main.ts`), ohne Build-Schritt. Erlaubt ist deshalb nur Syntax, die Node durch bloßes Entfernen der Typen ausführen kann (`erasableSyntaxOnly`), Importe tragen die Endung `.ts`.
- Die Node-Version steht nur in `src/auth/.nvmrc`. Aspire erzeugt daraus beim Publish das Dockerfile (`node:26-alpine`), die CI liest sie in `setup-auth` (siehe [ci.md](ci.md)).
- Paketversionen sind exakt gepinnt. `@better-auth/core` ist nur für `runWithTransaction` eine direkte Dependency und bleibt auf derselben Version wie `better-auth`.
- Datenbank: lokal über `MURMLI_URI` mit Passwort (Container), in Azure mit Entra-Token (`@azure/postgresql-auth` mit `DefaultAzureCredential`, Identity `backend-identity`) als Nutzer `PGUSER`. `PGUSER` ist der Principal-Name von `backend-identity`, der AppHost setzt ihn nur im Publish-Mode.
- Beim Start migriert `auth` sein Schema (siehe [database.md](database.md#schemas)), legt im Run-Mode den Test-Nutzer an und lauscht erst danach. Scheitert das, beendet es sich mit Fehler.
- Endpunkte:
  - `GET /health`
  - `GET /.well-known/openid-configuration`: minimales Discovery-Dokument (`issuer`, `jwks_uri`), `jwks_uri` zeigt auf `/api/auth/jwks`.
  - `POST /service/delete-user`: siehe „Konto löschen“.
  - Alles andere geht an Better Auth (`/api/auth/*`).
- Request-Bodies sind auf 64 KiB begrenzt, einen größeren per `Content-Length` angekündigten Body lehnt `auth` mit 413 ab. `/service/delete-user` liest höchstens 4096 Zeichen.
- Die Registrierung ist aus (`disableSignUp`). Better Auths Selbstlöschung `user.deleteUser` bleibt aus (Standard, Grund siehe „Entscheidungen“).

### Rate Limiting

- Better Auths Rate Limiting ist nur in Azure aktiv (`NODE_ENV=production`), im Speicher pro Replica. Die Client-IP liest es aus `X-Forwarded-For`.
- Offen, bis ein Mensch es nach dem ersten Deploy geprüft hat, danach entfällt dieser Punkt: ob Container Apps an ein vom Client gesendetes `X-Forwarded-For` anhängt. Prüfung: einmal normal und einmal mit gefälschtem Header anmelden, die IP der Session muss jeweils die echte sein und im Log darf keine Warnung „Rate limiting could not determine a client IP“ stehen. Hängt Container Apps an, muss `advanced.ipAddress.trustedProxies` gesetzt werden.

## Nutzer anlegen

- Lokal: Im Run-Mode reicht der AppHost `SEED_USER_EMAIL=test@murmli.de` und `SEED_USER_PASSWORD=password` an `auth`, das den Nutzer beim Start anlegt, wenn er fehlt. Postgres läuft lokal ohne Volume, nach einem Neustart des AppHost fehlt er also wieder.
- Azure: Ein Mensch legt Nutzer an, mit `az containerapp exec --name auth --resource-group rg-murmli-dev --command sh` und darin `npm run create-user -- <email> <name>` (fragt das Passwort ab). Agents führen das nie gegen Azure aus.

## Datenmodell

- Name, E-Mail, Passwort-Hashes, Sessions und Schlüssel liegen in den Tabellen von Better Auth im Schema `better-auth` (siehe [database.md](database.md#schemas)).
- `identity.users`: eigene, zufällige Id, Better-Auth-ID (`auth_user_id`, gleich `sub`, unique) und Anlagezeitpunkt. Keine E-Mail, kein Name.
- `identity.deleted_users`: Löschvermerke mit Better-Auth-ID (nicht eindeutig) und Löschzeitpunkt, siehe „Konto löschen“.
- `UserRegistry.GetOrCreateAsync` legt das Konto beim ersten Aufruf an, `GET /v1/users/me` liefert `{"id": "..."}`. Verhindert ein Löschvermerk die Anlage, antwortet es mit 401.
- Alle Nutzerdaten hängen an `identity.users.id`, nie an der Better-Auth-ID. Ein Wechsel des Identity-Providers berührt so nur die Zuordnung, nicht die Daten.

## Konto löschen

`DELETE /v1/users/me` ruft `UserRegistry.DeleteAsync` auf und antwortet mit 204, wenn alles gelöscht ist. Die Schritte laufen ohne gemeinsame Transaktion, jeder lässt sich wiederholen:

1. Die eigene Id des Kontos lesen, nur für den Log. Sie fehlt, wenn der Nutzer nie `GET /v1/users/me` aufgerufen hat.
2. Einen Löschvermerk (Better-Auth-ID, Zeitpunkt) in `identity.deleted_users` schreiben.
3. Das Konto in `identity.users` löschen. Nutzerdaten in Postgres hängen per Fremdschlüssel mit `ON DELETE CASCADE` an `identity.users` und verschwinden mit. Daten außerhalb von Postgres (derzeit keine) muss `DeleteAsync` ausdrücklich vor dem Aufruf von `auth` löschen.
4. Vermerke löschen, die älter als 24 Stunden sind.
5. Den Nutzer in Better Auth löschen: `POST <AuthService:BaseAddress>/service/delete-user` mit dem Service-Key und `{"userId": "<Better-Auth-ID>"}`, Timeout 10 s. Die ID steht im Body, nicht in der Adresse, und damit weder in den Logs der HttpClientFactory noch im HttpClient-Span. Jede 2xx-Antwort gilt als Erfolg, jeder andere Status, ein Timeout oder ein Verbindungsfehler führt zu 502.

`auth` löscht Sessions, Accounts und den Nutzer mit Better Auths eigenem Code (`internalAdapter.deleteUser`) in einer Transaktion: Scheitert ein Teil, bleibt der Nutzer vollständig erhalten. Es antwortet mit 204, auch für unbekannte IDs, mit 401 ohne gültigen Service-Key und mit 400 bei ungültigem Body.

Scheitert ein Schritt, kann sich der Nutzer weiter anmelden und die Löschung wiederholen, auch mit demselben Token. Der Endpunkt reicht `RequestAborted` nicht weiter (siehe „Entscheidungen“).

### Löschvermerk und `iat`

- Lokal geprüfte JWTs bleiben bis `exp` plus 5 Minuten Clock Skew von JwtBearer gültig, bei 15 Minuten Lebensdauer rund 20 Minuten. Ohne Vermerk legte `GET /v1/users/me` mit einem solchen Token nach der Löschung sofort ein neues Konto an.
- `GetOrCreateAsync` legt kein Konto an, wenn ein Vermerk für die Better-Auth-ID existiert, der nicht vor dem `iat` des Tokens liegt. Fehlt `iat`, genügt irgendein Vermerk. Ein vorhandenes Konto liefert es immer.
- Ein neues JWT nach einem gescheiterten Aufruf von `auth` hat ein späteres `iat` und darf ein Konto anlegen, damit der Nutzer die Löschung wiederholen kann.
- Vermerke bleiben 24 Stunden, weit über den rund 20 Minuten. Aufgeräumt wird erst bei der nächsten Löschung.
- Restrisiko: Eine Kontoanlage parallel zu einer laufenden Löschung desselben Nutzers hinterlässt ein leeres Konto, das nur die Better-Auth-ID trägt.

### Service-Key

- Die API schickt `auth-service-key` als `Authorization: Bearer <Service-Key>`, `auth` vergleicht ihn in konstanter Zeit mit `AUTH_SERVICE_KEY`. `/service/delete-user` hängt am öffentlichen Ingress, nur der Service-Key schützt ihn.
- Der Service-Key lässt sich jederzeit ersetzen, der neue Wert wirkt mit dem nächsten Deploy.
- Scheitert der Aufruf, loggt die API nur den Statuscode (`AuthUserDeletionFailed`), nie den Antwort-Body.

## Logs und Telemetrie

- Der Nutzer erscheint in den Logs der API nur mit der eigenen Id (`UserId`). `sub`, E-Mail und Token loggt der Code nie.
- Mit `Telemetry:RecordSensitiveData` (lokal und in Azure Dev) steht die Better-Auth-ID als SQL-Parameterwert in den Command-Logs von EF Core.
- Telemetrie von `auth` siehe [telemetry.md](telemetry.md#auth).

## Tests

- `Murmli.Backend.Api.Tests` startet die API ohne `MetadataAddress`, mit eigenem Issuer (`https://auth.test`), der Audience `murmli-api` und einem zufälligen symmetrischen Schlüssel über `SigningKeys` (`TestTokens`). So läuft der echte Prüfpfad des Handlers, ohne Metadatenabruf.
- `ApiHost` ersetzt den Primary Handler aller HttpClients durch `AuthServiceHttpMessageHandler` (zeichnet Anfragen an `auth` auf, Statuscode einstellbar) und die Uhr durch einen `FakeTimeProvider`. `TestTokens.Create` setzt `iat` auf Wunsch, `exp` und `nbf` kommen weiter aus der echten Zeit, weil JwtBearer dafür die Systemuhr nutzt.
- Echte Tokens gibt es nur im End-to-End-Test `AuthenticationTests` in `Murmli.Backend.Aspire.Tests`: Er meldet den Test-Nutzer am lokal gestarteten `auth` an, holt das JWT und löscht das Konto über die API, außerdem prüft er, dass Registrierung und `/service/delete-user` ohne Service-Key scheitern. Die Tests einer Collection teilen sich eine `AppHostFixture`, so läuft Aspires npm-Installer nie parallel. Kein Test spricht mit Azure.
- `Auth/ExtensionsTests` in `Murmli.Backend.Aspire.Tests` prüft die Trennung von Publish- und Run-Mode (`PGUSER` nur im Publish-Mode, Test-Nutzer und `RequireHttpsMetadata=false` nur im Run-Mode) und dass Issuer und Audience von API und `auth` übereinstimmen.
- Dass `/health` ohne Anmeldung erreichbar bleibt, deckt `HealthTests` in `Murmli.Backend.Aspire.Tests` ab.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| JWT lokal prüfen | Verworfen: Prüfung durch Better Auth pro Anfrage (`get-session`, also Introspection). Sie kostete einen HTTP-Aufruf pro Anfrage, die API fiele mit `auth` aus (doppelter Kaltstart bei Scale to Zero) und bräuchte einen eigenen Authentication-Handler. Folge: Abmelden wirkt erst mit dem Ablauf des JWT (höchstens rund 20 Minuten), gegen eine neue Kontoanlage nach dem Löschen hilft der Löschvermerk. |
| Eigenes Discovery-Dokument in `auth` | Better Auths JWT-Plugin liefert nur `/api/auth/jwks`, per Konfiguration lädt JwtBearer Schlüssel nur über ein Discovery-Dokument. Verworfen: das Plugin oauth-provider, es macht Better Auth zum OAuth-Server. Verworfen: ein eigener `ConfigurationManager` in .NET, Code statt Konfiguration. |
| ES256 statt Better Auths Standard EdDSA | Microsoft.IdentityModel kann EdDSA nicht prüfen ([AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet#2426](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/2426), offen), OKP-Schlüssel fielen still weg. Gespeicherte Schlüssel behalten ihren Algorithmus, deshalb bleibt `jwks.keyPairConfig.alg` bei ES256. |
| Konfiguration nur über die Bindung von `Authentication:Schemes:Bearer` | `AddJwtBearer` bindet die Sektion selbst und baut daraus `TokenValidationParameters`, inklusive `SigningKeys`. Ein im Code ersetztes `TokenValidationParameters` verwirft Issuer, Audiences und Schlüssel aus der Konfiguration. Der Delegate in `AddJwtBearer` läuft nach der Bindung. |
| `MapInboundClaims = false` | Sonst benennt der Handler `sub` in `ClaimTypes.NameIdentifier` um. Der Code liest `sub` direkt (`GetAuthUserId()`). |
| Fallback-Policy mit `RequireClaim("sub")` | Schützt jeden Endpunkt ohne eigene Auth-Metadaten, auch künftige. `sub` ist damit in jedem geschützten Endpunkt vorhanden. Unter .NET 11 fügt `WebApplication` `UseAuthentication` und `UseAuthorization` selbst ein. |
| API greift nie auf die Tabellen von Better Auth zu | Hooks und Plugin-Logik (etwa ein künftiger Widerruf der Apple-Tokens) laufen nur in Better Auths Code. Verworfen: die Tabellen aus der API lesen oder löschen. Die Trennung ist Konvention: Technisch könnte die gemeinsame Rolle von `backend-identity` sie lesen, dass `api` und `product-import` so an Passwort-Hashes und Sessions kämen, ist ein akzeptiertes Risiko. |
| Get-or-create über den Unique-Index | EF Core 10 kann kein Upsert. Bei gleichzeitigen ersten Anfragen gewinnt ein Insert, die anderen scheitern mit Unique Violation, leeren den Change Tracker und lesen das Konto. Hat eine gleichzeitige Löschung es inzwischen entfernt, antworten sie mit 401. Die Unique Violation ist nicht transient, die Retry-Strategie der Aspire-Integration wiederholt sie nicht. EF Core loggt dabei für jede unterlegene Anfrage das gescheiterte Kommando und das gescheiterte Speichern als Error. Das ist erwartet und wird nicht unterdrückt. |
| `Guid.NewGuid()` statt Version 7 | Version 7 trüge den Anlagezeitpunkt in der Id, die später auch als pseudonyme Kennung gegenüber Drittanbietern dient. |
| Löschvermerk mit `iat` | Verworfen: bei der Kontoanlage prüfen, ob der Nutzer in Better Auth noch existiert. Die API hat keinen Lesezugriff auf Better Auth. |
| Löschen über `/service/delete-user` mit Service-Key | Verworfen: Better Auths eingebautes `/delete-user` (`user.deleteUser`, bleibt deshalb aus), es braucht die Session des Nutzers und erlaubte Nutzern die Löschung an der API vorbei. Verworfen: `remove-user` des Admin-Plugins, es braucht einen Admin-Nutzer mit Passwort-Secret und öffnet Endpunkte für Ban, Impersonation und Nutzerlisten am öffentlichen Ingress. |
| Better Auth als letzter Schritt der Löschung | Verworfen: zuerst in Better Auth löschen. Scheitert danach ein Schritt, kann sich der Nutzer nicht mehr anmelden und die Löschung nicht wiederholen. |
| Löschung und Anlage in `auth` über `runWithTransaction` | `internalAdapter.deleteUser` löscht Sessions, Accounts und Nutzer in getrennten Statements, ohne Transaktion bliebe bei einem Fehler ein Nutzer ohne Anmeldemöglichkeit zurück. Verworfen: `context.adapter.transaction`, der `internalAdapter` nimmt die Transaktion nur aus dem AsyncLocalStorage, den `runWithTransaction` (`@better-auth/core/context`) setzt. Eine offene Transaktion hält ihren pg-Client ausgecheckt, deshalb hängt an jedem Client ein `error`-Listener: Ohne ihn beendet ein Verbindungsabbruch den Prozess. |
| Mehrere Vermerke pro Nutzer | Verworfen: Upsert, EF Core 10 kann es nicht. Ein Insert reicht auch bei doppeltem Tippen und bei Wiederholungen. |
| Löschung ohne `RequestAborted` | Die App darf sich direkt nach dem Tippen schließen, eine begonnene Löschung soll trotzdem durchlaufen. |
| Nutzer in Azure per `az containerapp exec` und `npm run create-user` | Die Registrierung bleibt aus. Verworfen: Admin-Plugin oder zeitweise offene Registrierung. Verworfen: ein lokales Skript gegen Postgres in Azure, die Firewall lässt nur Azure zu. |
