# Authentifizierung

Better Auth läuft als Node-Dienst `auth` (`src/auth`), stellt kurzlebige JWTs aus, die API prüft sie lokal.

## Anmeldung

1. `POST /api/auth/sign-in/email` (E-Mail, Passwort) oder `POST /api/auth/sign-in/social` (ID-Token von [Apple bzw. Google](#apple-und-google)). Signiert ist das Session-Token nur im Header `set-auth-token` (Plugin `bearer`, `requireSignature: true`), nicht im Body.
2. `GET /api/auth/token` mit `Authorization: Bearer <Session-Token>` liefert das JWT: ES256, 15 Minuten gültig, nur `sub` (Better-Auth-ID), `iss` (`BETTER_AUTH_URL`), `aud` (`murmli-api`), `iat`, `exp`.
3. API-Aufrufe mit `Authorization: Bearer <JWT>`, vor Ablauf neues JWT per Schritt 2.
4. `POST /api/auth/sign-out`, sonst gilt die Session 30 Tage ohne Nutzung (`session.expiresIn`). Jeder Abruf eines JWT (`GET /api/auth/token`) mehr als einen Tag nach der letzten Verlängerung (`updateAge`) verlängert sie.

Was die App anbietet: [app.md](app.md#anmeldung).

## Apple und Google

- Better Auth prüft Signatur, Issuer, Audience (Apple: `Apple:BundleIdentifier`, Google: `Google:WebClientId`, kein Geheimnis, in der App `serverClientId`) und Alter (höchstens 1 Stunde) des ID-Tokens.
- Apples `nonce` muss dem rohen Nonce aus der Anfrage an `auth` oder dessen SHA-256 (hex) gleichen. Better Auth vergibt keine Nonces, Tokens bleiben trotz Nonce bis zum Höchstalter wiederverwendbar.
- Die erste Anmeldung legt den Nutzer mit leerem Namen an (`disableSignUp` gilt nur für E-Mail).
- Better Auth verlangt eine E-Mail pro Nutzer (sonst 401), `mapProfileToUser` setzt immer `<sub>@apple.placeholder.invalid` bzw. `<sub>@google.placeholder.invalid` (nie zugestellt, pro Anbieterkonto eindeutig, daher nie implizit mit anderen Nutzern verknüpft), bei Google auch leeren Namen, kein Bild, `emailVerified` false.
- Better Auth speichert weder ID-Token (`databaseHooks` für `account` setzen es auf `NULL`) noch Refresh-Token und widerruft nie beim Anbieter (Apple: [#51](https://github.com/Murmli/MurmliV2/issues/51)).

### Sign in with Google

- Einrichtung (nur ein Mensch): Google-Cloud-Projekt „Murmli Dev“, getrennt von [„Murmli Google Play“](google-play.md#einrichtung-bei-google-play), mit Google Auth Platform, Zielgruppe „Extern“, Status „Testing“ (Anmeldung mit Basisdaten braucht keine Testnutzer), Web-Client (`Google:WebClientId`) und je einem Android-Client für `de.murmli.app.dev` pro SHA-1 aller App-Signaturschlüssel von Google Play samt „Previous app signing keys“ (Play signiert ältere Android-Versionen wie 13 weiter damit) und des Upload-Schlüssels. Debug-Schlüssel bekommen keinen, Debug-Builds melden sich daher nicht mit Google an.
- Fehlt ein SHA-1, wirkt die Anmeldung wie ein Abbruch (`UNREGISTERED_ON_API_CONSOLE` im `adb logcat` der Play-Dienste). SHA-1 der installierten App: APK per `adb shell pm path de.murmli.app.dev` und `adb pull` holen, dann `apksigner verify --print-certs` (Android Build-Tools), nicht `keytool -printcert -jarfile` (Play-APKs ohne v1-Signatur).
- Entschieden, noch nicht eingerichtet: eigenes Google-Cloud-Projekt für Produktion (Googles OAuth-Richtlinien verlangen Trennung von Test und Produktion), entfällt, sobald es hier steht.

## Konfiguration

- Lokal lauscht `auth` fest auf Port 5021 (stabile Adresse etwa für Flavor [`local`](app.md#flavors)), in Azure 0 bis 1 Replica.
- `better-auth-secret` (`BETTER_AUTH_SECRET`) verschlüsselt die privaten Schlüssel in `jwks` und signiert Session-Tokens, ein neuer Wert macht Schlüssel unlesbar und Sessions ungültig. Rotation über `BETTER_AUTH_SECRETS` braucht eine AppHost-Änderung.
- `auth-service-key` (`AUTH_SERVICE_KEY`) ist der einzige Schutz von `POST /service/delete-user` am öffentlichen Ingress, jederzeit ersetzbar (wirkt ab dem nächsten Deploy).
- `RequireHttpsMetadata=false` bekommt die API nur im Run-Mode, `auth` spricht lokal nur http.

## Dienst `auth`

- Node führt TypeScript ohne Build per Type Stripping aus (`node src/main.ts`): nur Syntax, die ohne Typen läuft (`erasableSyntaxOnly`), Importe mit `.ts`.
- Node-Version nur in `src/auth/.nvmrc`, daraus erzeugt Aspire beim Publish das Dockerfile.
- In Azure meldet sich `auth` per Entra-Token als `backend-identity` bei Postgres an (`PGUSER`, nur im Publish-Mode).
- Vor dem Lauschen [migriert](database.md#schemas) `auth` sein Schema und legt den [Test-Nutzer](#nutzer-anlegen) an, scheitert das, endet es mit Fehler.

### Rate Limiting

- Nur in Azure (`NODE_ENV=production`), im Speicher pro Replica, Client-IP aus `X-Forwarded-For`.
- Offen, bis ein Mensch nach dem ersten Deploy prüft, ob Container Apps an ein vom Client gesendetes `X-Forwarded-For` anhängt (dann entfällt der Punkt): normal und mit gefälschtem Header anmelden, beide Sessions müssen die echte IP tragen, kein Log „Rate limiting could not determine a client IP“. Falls ja, `advanced.ipAddress.trustedProxies` setzen.

## Nutzer anlegen

- Lokal (Run-Mode): `auth` legt beim Start `test@murmli.de` mit Passwort `password` an, falls er fehlt (Postgres ohne Volume).
- Azure (nur ein Mensch): `az containerapp exec --name auth --resource-group rg-murmli-dev --command sh`, darin `npm run create-user -- <email> <name>` (fragt das Passwort ab).

## Datenmodell

- `identity.users` trägt nur zufällige eigene Id, `auth_user_id` (gleich `sub`, unique), Anlagezeitpunkt, den Beginn der Löschung (`deletion_started_at`, die Markierung „wird gelöscht“, [Konto löschen](#konto-löschen)) und den UTC-Tag der letzten Anfrage (`last_request_on`); Name und E-Mail kennt nur Better Auth. Nutzerdaten hängen an der eigenen Id, ein Providerwechsel berührt so nur die Zuordnung.
- `last_request_on` setzt die Kontoanlage, danach schreibt ihn `UserTelemetryMiddleware` höchstens einmal am Tag: nur wenn der gelesene Tag fehlt oder vor dem heutigen UTC-Tag der Serveruhr liegt, mit derselben Bedingung im `UPDATE`, so schreibt von gleichzeitigen ersten Anfragen eines Tages nur eine. Jede angemeldete Anfrage mit Konto zählt, auch während der Löschung.
- `GET /v1/users/me` legt beim ersten Aufruf das Konto an, außer ein Löschvermerk (`identity.deleted_users`) verhindert es (dann 401). Für ein markiertes Konto schließt er die Löschung ab und antwortet mit 401, auch wenn `auth` dabei scheitert; ein Konto legt er dabei nie an ([Konto löschen](#konto-löschen)).
- Den persönlichen Bereich des Abgleichs und mit ihm die erste Einkaufsliste legt der Server beim ersten Abgleich des Kontos an, nicht mit dem Konto; Geräte erfahren beides nur über den Abgleich ([sync.md](sync.md#erster-abgleich-eines-kontos)). `POST /v1/sync` legt nie ein Konto an, ohne Konto und ab der Markierung „wird gelöscht“ antwortet er mit 410 `account-gone`.

## Konto löschen

`DELETE /v1/users/me` antwortet 204, wenn alles gelöscht ist. Die Schritte laufen ohne gemeinsame Transaktion, jeder ist wiederholbar ([#130](https://github.com/Murmli/MurmliV2/issues/130)):

1. Eigene Id zu `sub` lesen.
2. Löschvermerk (Better-Auth-ID, Zeitpunkt) schreiben.
3. Konto als „wird gelöscht“ markieren (`deletion_started_at`, `SyncAccountLifecycle.MarkDeletingAsync`), in einer Transaktion unter der Sperre seines persönlichen Bereichs im Abgleich. Von da an antwortet `/v1/sync` mit 410 `account-gone`, und kein Kommando legt mehr einen Bereich an ([sync.md](sync.md#kontolöschung)).
4. Eigene geteilte Bereiche löschen (`SyncAccountLifecycle.DeleteOwnedScopesAsync`), je Bereich in einer eigenen Transaktion, samt Datensätzen, Löschmarkierungen und Mitgliedseinträgen.
5. Nutzer in `auth` löschen (`POST /service/delete-user`, Timeout 10 s, ID im Body, damit sie nicht in Logs der HttpClientFactory und im HttpClient-Span landet). Kein 2xx, Timeout oder Verbindungsfehler ergeben 502, geloggt nur Statuscode bzw. Exception (`AuthUserDeletionFailed`), nie der Antwort-Body; das Konto bleibt markiert. `auth` antwortet auch für unbekannte IDs 204.
6. Einen neuen Löschvermerk mit dem Zeitpunkt nach der Löschung in `auth` schreiben, dann das Konto in `identity.users` löschen. Nutzerdaten folgen per `ON DELETE CASCADE`: der persönliche Bereich samt Datensätzen, die verarbeiteten Kommandos (`sync.processed_commands`) und was ein Abgleich parallel zur Markierung noch angelegt hat. Mitgliedseinträge des Kontos in fremden Bereichen verlieren per `ON DELETE SET NULL` nur ihre Verbindung zum Konto ([#131](https://github.com/Murmli/MurmliV2/issues/131)); vor dem Teilen gibt es keine.
7. Vermerke älter als 24 Stunden löschen (weit über die rund 20 Minuten JWT-Gültigkeit).

Die Schritte 4 bis 7 sind `UserRegistry.CompleteDeletionAsync`. Daten außerhalb von Postgres löscht es ausdrücklich vor dem Aufruf von `auth`; derzeit gibt es keine.

Scheitert ein Schritt nach der Markierung, bleibt das Konto markiert: `/v1/sync` antwortet mit 410, `GET /v1/users/me` mit 401. Die nächste Anmeldung (`GET /v1/users/me`) oder ein erneutes `DELETE /v1/users/me` schließt die Löschung ab, die Anmeldung antwortet danach mit 401, gleich wie `auth` antwortet. Für Nutzer, die nicht wiederkommen, und für einen Fehler in Schritt 6 nach der Löschung in `auth` (dann ist keine Anmeldung mehr möglich) schließt der [Aufräum-Lauf des Abgleichs](sync.md#aufbewahrung-und-aufräumen) sie ab: `StaleAccountDeletionContributor` ruft `CompleteDeletionAsync` für jedes Konto auf, das seit mehr als 10 Minuten markiert ist (die zuerst markierten zuerst, in Portionen zu `Sync:Cleanup:BatchSize`, bis keine mehr kommen, jedes Konto höchstens einmal je Lauf), mit `UserId` als Log-Scope, und loggt danach `StaleAccountDeletionCompleted` (2004, Information, `UserId`). Scheitert `auth`, bleibt das Konto bis zum nächsten Lauf markiert, und der Lauf macht mit den übrigen Konten weiter.

### Löschvermerk und `iat`

- Lokal geprüfte JWTs gelten bis `exp` plus 5 Minuten Clock Skew von JwtBearer, rund 20 Minuten. Ohne Vermerk legte `GET /v1/users/me` damit nach der Löschung sofort ein neues Konto an.
- Kein neues Konto, wenn ein Vermerk nicht vor dem `iat` des Tokens liegt (ohne `iat` genügt jeder Vermerk), ein vorhandenes Konto kommt immer, außer es ist als „wird gelöscht“ markiert: Dann schließt `GET /v1/users/me` die Löschung ab und antwortet mit 401 ([Konto löschen](#konto-löschen)). Ein JWT, das `auth` ausstellt, während das Konto markiert ist, legt danach kein Konto an: Der Vermerk aus Schritt 6 entsteht erst nach der Löschung in `auth` und liegt damit nach jedem `iat`, das der Nutzer noch vorzeigen kann.
- Restrisiko: Gab es beim Start der Löschung noch kein Konto, hinterlässt eine erste Kontoanlage parallel zur Löschung desselben Nutzers ein leeres Konto mit nur der Better-Auth-ID.

## Logs und Telemetrie

Siehe [`auth`](telemetry.md#auth). Akzeptierte Ausnahme: Mit `Telemetry:RecordSensitiveData` (nur lokal und in Azure Dev, dort nur Testdaten) steht die Better-Auth-ID als SQL-Parameterwert in den EF-Core-Command-Logs der [Konto-Id-Abfrage](telemetry.md#konto-id) jeder angemeldeten Anfrage sowie der Anweisungen von Kontoanlage und Kontolöschung (Suche der Kontozeile, Löschvermerke, Löschen der Kontozeile), auch wenn der Aufräum-Lauf eine Löschung abschließt.

## Tests

- `TestTokens` signieren mit zufälligem symmetrischem Schlüssel über `SigningKeys` statt `MetadataAddress` (echter Prüfpfad ohne Metadatenabruf), `exp` und `nbf` aus der Systemuhr, die JwtBearer statt des `FakeTimeProvider` von `ApiHost` nutzt.
- Span-Listener von `ApiHost` sind prozessweit, die Liste enthält auch Spans paralleler Hosts und ist erst nach Entsorgen des Hosts vollständig: danach auswerten, gefiltert per eigener Trace-ID im `traceparent`.
- Tests einer Collection in `Murmli.Backend.Aspire.Tests` teilen eine `AppHostFixture`, damit Aspires npm-Installer nie parallel läuft.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| JWT lokal prüfen | Verworfen: Introspection per `get-session` (HTTP-Aufruf pro Anfrage, API fiele mit `auth` aus, doppelter Kaltstart bei Scale to Zero). Folge: Abmelden wirkt erst nach bis zu rund 20 Minuten (Ablauf des JWT). |
| Eigenes Discovery-Dokument in `auth` | JwtBearer lädt Schlüssel per Konfiguration nur über Discovery, Better Auth liefert nur `/api/auth/jwks`. Verworfen: Plugin oauth-provider (macht Better Auth zum OAuth-Server), eigener `ConfigurationManager` (Code statt Konfiguration). |
| ES256 statt Better Auths Standard EdDSA | Microsoft.IdentityModel prüft kein EdDSA ([AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet#2426](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/2426), offen), OKP-Schlüssel fielen still weg. Gespeicherte Schlüssel behalten ihren Algorithmus. |
| Nur Bindung von `Authentication:Schemes:Bearer` | Ein im Code ersetztes `TokenValidationParameters` verwirft Issuer, Audiences und `SigningKeys` der Bindung, der Delegate in `AddJwtBearer` läuft danach. |
| Eine Policy mit `RequireClaim("sub")` als Default- und Fallback-Policy | Die Fallback-Policy schützt jeden Endpunkt ohne eigene Auth-Metadaten, auch unbekannte Routen. Die Default-Policy gilt für explizites `RequireAuthorization()` bzw. `[Authorize]` und verlangte sonst nur einen angemeldeten Nutzer, `GetAuthUserId()` setzt aber `sub` voraus und wirft sonst. Benannte Policies, Rollen und eigene Policy-Instanzen ersetzen die Default-Policy und brauchen `RequireClaim("sub")` selbst. |
| `MapInboundClaims = false` | Sonst wird `sub` zu `ClaimTypes.NameIdentifier`, der Code liest `sub` direkt. |
| API ohne Zugriff auf Better Auths Tabellen | Hooks und Plugin-Logik (etwa künftiger Widerruf von Apple-Tokens) laufen nur in Better Auth. Nur Konvention: Über die gemeinsame Rolle `backend-identity` könnten `api` und `product-import` Passwort-Hashes und Sessions lesen, akzeptiertes Risiko. |
| Get-or-create über den Unique-Index, mehrere Löschvermerke pro Nutzer | EF Core 10 kann kein Upsert: Bei gleichzeitigen ersten Anfragen gewinnt ein Insert, die übrigen lesen nach der Unique Violation (nicht transient, kein Retry) das Konto. EF Cores Error-Logs dazu sind erwartet und nicht unterdrückt. Doppelte Löschvermerke (doppeltes Tippen, Wiederholung) schaden nicht. |
| `Guid.NewGuid()` statt Version 7 | Version 7 trüge den Anlagezeitpunkt in der Id, die später auch Drittanbietern als pseudonyme Kennung dient. |
| Löschvermerk mit `iat` | Verworfen: bei der Kontoanlage in Better Auth prüfen, ob der Nutzer noch existiert, die API hat dort keinen Lesezugriff. |
| Löschen über `/service/delete-user` mit Service-Key | Verworfen: Better Auths `/delete-user` (`user.deleteUser`, braucht die Nutzer-Session, erlaubte Löschungen an der API vorbei), `remove-user` des Admin-Plugins (braucht Admin-Nutzer mit Passwort-Secret, öffnet Ban, Impersonation und Nutzerlisten am öffentlichen Ingress). |
| `auth` vor der Kontozeile, danach ein neuer Löschvermerk | Die markierte Zeile bleibt bis zur bestätigten Löschung in `auth` und ist das Signal, sie zu wiederholen; der Vermerk danach liegt nach jedem `iat`, das der Nutzer noch vorzeigen kann. Verworfen: `auth` zuletzt (ein Fehler dort ginge mit der Zeile verloren, und ein JWT aus der Zeit der Markierung legte zwischen dem Löschen der Zeile und dem Aufruf von `auth` ein neues Konto an). |
| Markierung „wird gelöscht“ vor dem ersten Löschen (Schritt 3), unter der Sperre des persönlichen Bereichs | Ohne sie legte ein zweites Gerät mitten in der Löschung noch eine Liste an, die danach niemandem gehört ([#130](https://github.com/Murmli/MurmliV2/issues/130)); das Anlegen eines Wurzel-Datensatzes liest die Markierung nach derselben Sperre ([sync.md](sync.md#kontolöschung)). |
| Markiertes Konto bei `GET /v1/users/me`: Löschung abschließen, dann 401 | Scheitert ein Schritt, bleibt die Anmeldung der Weg, die Löschung zu wiederholen ([#50](https://github.com/Murmli/MurmliV2/issues/50)). Keine Altersgrenze: Jeder Schritt ist neben einem laufenden `DELETE` unschädlich (mehrere Vermerke sind erlaubt, doppeltes Löschen trifft 0 Zeilen, `auth` antwortet für unbekannte IDs 204). Verworfen: 200 (die App meldete sich an, bekäme vom Abgleich 410, löschte alles und begänne von vorn), ein bloßes 401 (die Löschung hinge bis zum Aufräum-Lauf), Abschließen und danach Get-or-create (legte ein Konto für den gelöschten Better-Auth-Nutzer an). |
| Hängengebliebene Löschungen auch im Aufräum-Lauf, nach 10 Minuten | Der Nutzer hat die Löschung schon verlangt. Nutzer, die nie wiederkommen, und ein Fehler in Schritt 6 nach der Löschung in `auth` brauchen einen Weg ohne Anmeldung; die 10 Minuten lassen ein laufendes `DELETE` in Ruhe. Verworfen: nur der Aufräum-Lauf (der Nutzer wartete bis zum nächsten Lauf), nur Anmeldung und `DELETE` (Daten blieben für immer). |
| Hängengebliebene Löschungen in Portionen nach Markierung und Id, jedes Konto einmal je Lauf | Die Markierung ändert sich nach dem Setzen nie, die Reihenfolge ist fest; die nächste Portion beginnt nach dem letzten Konto der vorigen, so halten Konten, bei denen `auth` dauerhaft scheitert, die übrigen nicht auf. Verworfen: eine Portion je Lauf (scheiternde Konten blieben an ihrer Spitze), eine Liste übersprungener Konten wie bei abgehakten Artikeln (wächst mit jedem scheiternden Konto), Zähler der Versuche je Konto (braucht eine Spalte). |
| Tag der letzten Anfrage in `UserTelemetryMiddleware`, höchstens einmal am Tag | [#130](https://github.com/Murmli/MurmliV2/issues/130) zählt jede Anfrage, nicht nur den Abgleich; die Middleware hat das Konto ohnehin gelesen, das `UPDATE` mit Bedingung schreibt bei gleichzeitigen ersten Anfragen eines Tages nur einmal. Verworfen: nur im Abgleich, bei jeder Anfrage schreiben. |
| Löschung und Anlage in `auth` über `runWithTransaction` (`@better-auth/core/context`, deshalb direkte Dependency) | `internalAdapter.deleteUser` löscht in getrennten Statements, ein Fehler hinterließe einen nicht anmeldbaren Nutzer. Verworfen: `context.adapter.transaction`, `internalAdapter` nimmt die Transaktion nur aus `runWithTransaction` (AsyncLocalStorage). Jeder pg-Client braucht einen `error`-Listener, sonst beendet ein Verbindungsabbruch in offener Transaktion den Prozess. |
| Timeout von 10 s trotz Scale to Zero von `auth` | Akzeptiert: In Dev kann die erste Löschung nach einer Pause am Kaltstart scheitern (502, wiederholbar). Entschieden, noch nicht gebaut: Produktion mit mindestens einer Replica von `auth`, entfällt, sobald der AppHost das setzt. |
| Löschung ohne `RequestAborted` | Begonnene Löschungen laufen durch, auch wenn die App sofort schließt, ebenso ihr Abschluss in `GET /v1/users/me`. Ebenso der Abruf der Konto-Id in `UserTelemetryMiddleware` vor jedem Endpunkt, sonst begänne die Löschung nie (eine Index-Abfrage, Abbruch spart nichts). Verworfen: `RequestAborted` nur bei `DELETE` ignorieren (bei jedem neuen Endpunkt mitzudenken). |
| Telemetrie bleibt beim Löschen erhalten | Läuft mit der Aufbewahrungsfrist aus, die zufällige Id verliert mit dem Konto ihre Zuordnung. Verworfen: gezieltes Entfernen, unverhältnismäßig für pseudonyme Daten. |
| Zähler der Rate-Limits bleiben beim Löschen im Speicher | Sie enthalten nur Zählerstände zur eigenen Id und zu Geräte-IDs, ein neues Konto bekommt eine neue Id, und sie verfallen von selbst: die Fenster spätestens gut eine Minute nach der letzten gezählten Anfrage, Kommandos je Konto spätestens rund eine Stunde nach dem letzten Kommando. Verworfen: gezieltes Entfernen in `UserRegistry.CompleteDeletionAsync` (`PartitionedRateLimiter` kann einzelne Einträge nicht entfernen). |
| Nutzer in Azure per `npm run create-user`, Registrierung per E-Mail aus | Nur für Dev: ohne verdeckte Passworteingabe und E-Mail-Prüfung, Anmeldung nur mit anderen Clients (etwa Bruno), nie mit der App. Entschieden, noch nicht gebaut: Produktion nur mit Apple und Google, entfällt, sobald sie läuft. Verworfen: Admin-Plugin, zeitweise offene Registrierung, lokales Skript gegen Postgres in Azure (Firewall lässt nur Azure zu). |
| ID-Token aus nativem Dialog, Apple nur auf iOS, Google nur auf Android | Verworfen: Better Auths Redirect-Ablauf (braucht Client Secret, bei Apple aus Services ID und Schlüssel, und öffentliche Redirect-URL, öffnet Browser statt nativem Dialog). Apple auf Android ginge nur so (eigener Endpunkt in `auth`, Custom Tab), Google auf iOS bräuchte iOS-Client und GoogleSignIn-SDK. Folge: Apple-Konten sind auf Android unerreichbar, Google-Konten auf iOS. |
| Keine Scopes bei Apple, Platzhalter-E-Mail | Murmli braucht von Anbietern weder Name noch Bild noch E-Mail. Verworfen: Scope `email` bei Apple, er lieferte eine echte oder Relay-Adresse, die Murmli nicht nutzt. Entschieden, noch nicht gebaut (entfällt mit dem Bau): ein in der App frei wählbares Pseudonym als Name. Falls doch E-Mail nötig: neue Apple-Nutzer per Scope `email`, vorhandene in der App (Apple liefert Name und E-Mail nur bei der ersten Autorisierung), Google-Nutzer aus dem ID-Token (vorhandene ab nächster Anmeldung, nur mit `overrideUserInfoOnSignIn`). |
| Google ohne Nonce | Nur `init` nimmt einen Nonce an, laut Vertrag von `google_sign_in` genau einmal aufgerufen (`google_sign_in_android` 7.2.19 prüft das nicht), alle Anmeldungen bis zum Neustart trügen denselben. Wiederverwendung verhinderte er ohnehin nicht ([oben](#apple-und-google)). |
| Keine ID-Tokens der Anbieter speichern | Googles ID-Token trägt E-Mail, Name und Bild. Gelesen wird das gespeicherte nur von `createEndSessionURL` (unterstützt kein eingebauter Provider), `/refresh-token` (braucht ein Refresh-Token, der native Ablauf speichert keins) und `/get-access-token` (gibt es nur zurück, die App nutzt es nicht). Verworfen: `account.encryptOAuthTokens`, verschlüsselt in Better Auth 1.7.25 kein `idToken`. |
| Authorization Code von Apple vorerst nicht eingelöst | Einlösen gegen ein Refresh-Token braucht Schlüssel und Client Secret, Better Auths Identity-Token-Weg kann es nicht. Bis dahin lassen sich Konten nur in den Apple-Einstellungen trennen. Entschieden, noch nicht gebaut: Einlösen mit [#51](https://github.com/Murmli/MurmliV2/issues/51), dann entfällt die Zeile. |
| `--omit=dev` nur im Publish-Mode | Aspire 13.6.5 erzeugt für `AddNodeApp` keine eigene Stage für Produktions-Abhängigkeiten. Verworfen: eigenes Dockerfile in `src/auth` (schaltet die Generierung ab), `WithDockerfileBuilder` (experimentell). |
