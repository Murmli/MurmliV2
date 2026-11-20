# Authentifizierung

Better Auth läuft als eigener Node-Dienst `auth` (`src/auth`). Angemeldete Clients holen dort ein kurzlebiges JWT, die API prüft es lokal mit den von `auth` veröffentlichten Schlüsseln, ohne Introspection.

## Anmeldung

1. Anmelden, das signierte Session-Token steht nur im Header `set-auth-token` (Plugin `bearer` mit `requireSignature: true`, das Token im Body ist unsigniert):
   - `POST /api/auth/sign-in/email` mit E-Mail und Passwort: die App im Flavor `local`, Tests, andere lokale Clients und Nutzer aus `create-user`.
   - `POST /api/auth/sign-in/social` mit `{"provider":"apple","idToken":{"token":<Identity-Token>,"nonce":<roher Nonce>}}` aus der iOS-App bzw. `{"provider":"google","idToken":{"token":<ID-Token>}}` aus der Android-App, beide außer im Flavor `local` ([Apple und Google](#apple-und-google)).
2. `GET /api/auth/token` mit `Authorization: Bearer <Session-Token>` liefert das JWT: ES256, 15 Minuten gültig, nur `sub` (Better-Auth-ID), `iss` (`BETTER_AUTH_URL`, eigener Endpunkt von `auth`), `aud` (`murmli-api`), `iat` und `exp`.
3. API-Aufrufe mit `Authorization: Bearer <JWT>`, vor Ablauf ein neues JWT über Schritt 2.
4. `POST /api/auth/sign-out`. Ohne Abmeldung gilt eine Session 7 Tage und verlängert sich bei Nutzung (Standard von Better Auth).

Welche Anmeldung die App je nach Flavor und Plattform zeigt, steht in [app.md](app.md#anmeldung).

## Apple und Google

- Better Auths Provider prüft das ID-Token (bei Apple Identity-Token): Signatur gegen die Schlüssel des Anbieters (bei jeder Anmeldung abgerufen, ohne Cache), Issuer, Audience (`clientId`) und Alter höchstens 1 Stunde. Kein Client Secret nötig.
- Better Auth vergibt keine Nonces, ein Token lässt sich also innerhalb der Stunde erneut verwenden, auch mit Apples Nonce.
- Die erste Anmeldung legt den Nutzer an (`disableSignUp` gilt nur für die Registrierung per E-Mail), mit leerem Namen.
- Better Auth verlangt eine E-Mail pro Nutzer (ohne lehnt es die Anmeldung mit 401 ab), Murmli speichert aber keine E-Mail, keinen Namen und kein Bild der Anbieter. `mapProfileToUser` setzt deshalb immer den Platzhalter `<sub>@apple.placeholder.invalid` bzw. `<sub>@google.placeholder.invalid` (Muster aus Better Auths Doku „Handling Providers Without Email“), nie eine E-Mail aus dem Token, bei Google zusätzlich leeren Namen, kein Bild und `emailVerified` false (Googles ID-Token trägt E-Mail, Name und Bild). An die reservierte Domain `.invalid` wird nie zugestellt.
- Der Platzhalter ist pro Konto beim Anbieter eindeutig, Better Auth verknüpft eine Anmeldung deshalb nie implizit per E-Mail mit einem anderen Nutzer.
- Die `databaseHooks` für `account` setzen `idToken` bei jedem Anlegen und Ändern auf `NULL`: Better Auth speichert kein ID-Token der Anbieter ([Entscheidungen](#entscheidungen)) und kein Refresh-Token, und es widerruft nie beim Anbieter (Apple: [#51](https://github.com/Murmli/MurmliV2/issues/51)).

### Sign in with Apple

- Audience ist die Bundle-ID (`Apple:BundleIdentifier`), Schlüssel von `https://appleid.apple.com/auth/keys`.
- Der Claim `nonce` muss dem rohen Nonce oder dessen SHA-256 (hex) gleichen: Die App schickt den Hash an Apple und den rohen Nonce an `auth`.
- Keine Services ID und kein Schlüssel nötig. Die App fragt bei Apple keine Scopes an.

### Sign in with Google

- Audience ist die Web-Client-ID (`Google:WebClientId`, kein Geheimnis), die die App als `serverClientId` an Google gibt, Schlüssel von `https://www.googleapis.com/oauth2/v3/certs`. Ohne Nonce ([Entscheidungen](#entscheidungen)).
- Einrichtung bei Google (nur ein Mensch): Google-Cloud-Projekt „Murmli Dev“, getrennt von „Murmli Google Play“ mit dem Service-Account für Uploads ([google-play.md](google-play.md#einrichtung-bei-google-play)). Darin die Google Auth Platform mit Zielgruppe „Extern“ im Status „Testing“ (Sign in with Google mit Basisdaten braucht keine Testnutzer), ein Web-Client, dessen ID `Google:WebClientId` ist, und Android-Clients für `de.murmli.app.dev`, je einer pro SHA-1: alle App-Signaturschlüssel von Google Play, auch die unter „Previous app signing keys“ (Play signiert für ältere Android-Versionen, etwa Android 13, weiter mit einem früheren Schlüssel), und der Upload-Schlüssel, keiner für Debug-Schlüssel.
- Mit einem Debug-Schlüssel signierte Builds (etwa `fvm flutter run --flavor development`) können sich deshalb nicht mit Google anmelden, nur Installationen aus dem internen Test und mit dem Upload-Schlüssel signierte Builds ([app.md](app.md#anmeldung)).
- Wirkt die Anmeldung mit Google wie ein Abbruch, steht bei fehlendem SHA-1 `UNREGISTERED_ON_API_CONSOLE` im `adb logcat` der Play-Dienste. Den SHA-1 der installierten App zeigt `apksigner verify --print-certs` (Android Build-Tools) für die APK aus `adb shell pm path de.murmli.app.dev` und `adb pull`. `keytool -printcert -jarfile` liest Play-APKs nicht, sie haben keine v1-Signatur.
- Entschieden, noch nicht eingerichtet: Produktion bekommt ein eigenes Google-Cloud-Projekt, weil Googles OAuth-Richtlinien getrennte Projekte für Test und Produktion verlangen. Der Punkt entfällt, sobald es eingerichtet ist und hier steht.

## Konfiguration

- Lokal lauscht `auth` fest auf Port 5021 als stabile Adresse für lokale Clients (etwa den Flavor `local`, siehe [app.md](app.md#flavors)). In Azure läuft es mit 0 bis 1 Replica.
- `better-auth-secret` (`BETTER_AUTH_SECRET`) verschlüsselt die privaten Schlüssel in `jwks` und signiert die Session-Tokens. Ein neuer Wert machte die gespeicherten Schlüssel unlesbar und alle Sessions ungültig. Rotation ginge nur über `BETTER_AUTH_SECRETS` und bräuchte eine Änderung am AppHost.
- `auth-service-key` (`AUTH_SERVICE_KEY`) ist das einzige, was `POST /service/delete-user` am öffentlichen Ingress schützt. Er lässt sich jederzeit ersetzen, der neue Wert wirkt mit dem nächsten Deploy.
- `RequireHttpsMetadata=false` bekommt die API nur im Run-Mode, weil `auth` lokal nur http spricht.
- `AddAuth()` setzt `APPLE_BUNDLE_IDENTIFIER` aus `Apple:BundleIdentifier` und `GOOGLE_WEB_CLIENT_ID` aus `Google:WebClientId` (`appsettings.Development.json` für `aspire run` und Tests, `appsettings.azure-<name>.json` pro Umgebung), derzeit überall `de.murmli.app.dev` (Flavor `development`) bzw. der Web-Client aus dem Google-Cloud-Projekt „Murmli Dev“. Fehlt ein Wert oder ist er leer, wirft der AppHost.

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
- Apple und Google: Better Auth legt den Nutzer bei der ersten Anmeldung an ([Apple und Google](#apple-und-google)).

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
- `AuthenticationTests` zeigt auch, dass die Provider für Apple und Google konfiguriert sind: Ein ungültiges ID-Token ergibt jeweils 401 statt 404, ohne den Anbieter anzufragen. `Auth/ExtensionsTests` prüft, dass Bundle-ID und Web-Client-ID bei `auth` ankommen und der AppHost ohne sie wirft.

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
| Nutzer in Azure per `npm run create-user`, Registrierung per E-Mail aus | Nur für Dev, deshalb ohne verdeckte Passworteingabe und ohne Prüfung der E-Mail. Die App bietet die Anmeldung per E-Mail nur im Flavor `local` an, diese Nutzer melden sich deshalb nur mit anderen Clients an (etwa Bruno). Entschieden, noch nicht gebaut: eine Produktionsumgebung nur mit der Anmeldung über Apple und Google, ohne Passwort-Nutzer, die Notiz entfällt, sobald sie läuft. Verworfen: Admin-Plugin, zeitweise offene Registrierung, lokales Skript gegen Postgres in Azure (die Firewall lässt nur Azure zu). |
| Apple und Google über das ID-Token des nativen Dialogs, ohne Client Secret | Verworfen: Better Auths Redirect-Ablauf, braucht ein Client Secret (bei Apple aus Services ID und Schlüssel) und eine öffentliche Redirect-URL und öffnet einen Browser statt des nativen Dialogs. |
| Apple nur auf iOS, Google nur auf Android (im Flavor `local` keins von beiden, siehe [app.md](app.md#entscheidungen)) | Apple auf Android bräuchte den Web-Ablauf mit Services ID, Schlüssel, eigenem Redirect-Endpunkt in `auth` und einem Custom Tab statt eines nativen Dialogs. Google auf iOS bräuchte einen iOS-Client bei Google und das GoogleSignIn-SDK in der App. Folge: Ein mit Apple angelegtes Konto ist auf Android nicht erreichbar, ein mit Google angelegtes nicht auf iOS. |
| Keine Scopes bei Apple, Platzhalter-E-Mail bei Apple und Google | Murmli braucht von den Anbietern weder Namen noch E-Mail noch Bild. Den Namen fragt die App selbst ab, als frei wählbares Pseudonym (entschieden, noch nicht gebaut, der Vermerk entfällt, sobald die App den Namen abfragt). Braucht Murmli doch eine E-Mail, bekommen neue Nutzer den Scope `email` bei Apple, bei Google übernimmt `mapProfileToUser` sie aus dem ID-Token (vorhandene Google-Nutzer nur mit `overrideUserInfoOnSignIn`, bei ihrer nächsten Anmeldung), vorhandene Apple-Nutzer tragen sie in der App ein: Apple fragt Name und E-Mail nur bei der ersten Autorisierung ab. Verworfen: der Scope `email` bei Apple, er speicherte eine echte oder eine Relay-Adresse, die Murmli nicht nutzt. |
| Google ohne Nonce | Den Nonce nimmt nur `init` an, das laut Vertrag von `google_sign_in` genau einmal läuft (`google_sign_in_android` 7.2.19 prüft das nicht), alle Anmeldungen bis zum Neustart trügen also denselben. Weil Better Auth keine Nonces vergibt, verhinderte auch ein frischer nicht, dass ein Token erneut verwendet wird. |
| Keine ID-Tokens der Anbieter speichern (`databaseHooks` für `account`) | Googles ID-Token trägt E-Mail, Name und Bild, Murmli speichert nichts davon. Better Auth braucht das gespeicherte Token nicht: Die Anmeldung prüft das Token der Anfrage, gelesen wird es nur beim Abmelden über `createEndSessionURL` (kann kein eingebauter Provider), beim Erneuern über `/refresh-token` (braucht ein Refresh-Token, das der native Ablauf nie speichert) und von `/get-access-token`, das es dem angemeldeten Nutzer nur zurückgibt (die App ruft es nie auf). Verworfen: `account.encryptOAuthTokens`, verschlüsselt in Better Auth 1.7.25 nur Access- und Refresh-Token, nicht `idToken`. |
| Authorization Code von Apple vorerst nicht eingelöst | Das Einlösen gegen ein Refresh-Token braucht Schlüssel und Client Secret, Better Auths Weg über das Identity-Token kann es nicht. Bis dahin angelegte Konten lassen sich nur in den Apple-Einstellungen trennen. Entschieden, noch nicht gebaut: Einlösen mit [#51](https://github.com/Murmli/MurmliV2/issues/51), dann entfällt diese Zeile. |
| `--omit=dev` nur im Publish-Mode | Aspire 13.6.5 erzeugt für `AddNodeApp` keine eigene Stage für Produktions-Abhängigkeiten. Im Run-Mode entfernte Aspires Installer damit Biome und tsc aus `src/auth/node_modules`. Verworfen: eigenes Dockerfile in `src/auth` (schaltet die Generierung ab), `WithDockerfileBuilder` (experimentell). |
