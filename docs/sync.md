# Abgleich

Vertrag zwischen App und API für den Abgleich ([#122](https://github.com/Murmli/MurmliV2/issues/122)), Aufbau und Begriffe in [#121](https://github.com/Murmli/MurmliV2/issues/121). Ein einziger Endpunkt nimmt die wartenden Kommandos eines Geräts an und liefert in derselben Antwort, was sich seit dessen Token geändert hat; ein neuer Datentyp braucht keinen neuen Endpunkt. Der Server entscheidet: Das Gerät wendet eigene Änderungen sofort an, Konfliktregeln gibt es nur am Server. Er liefert den aktuellen Stand geänderter Datensätze statt Änderungsfolgen, ein vollständiger Stand repariert jede Abweichung. Jede Anfrage darf beliebig oft wiederholt werden, ohne dass etwas doppelt wirkt, und kein Kommando hält die Warteschlange auf Dauer auf.

Entschieden, noch nicht gebaut: das Verhalten der App, das dieses Dokument beschreibt, soweit kein eigener Vermerk ein anderes Issue nennt. Entfällt, sobald der Sync-Kern der App aus [#124](https://github.com/Murmli/MurmliV2/issues/124) gebaut ist.

| Begriff | Im Vertrag | Bedeutung |
| --- | --- | --- |
| Bereich | `scopeId`, Block in `scopes` | Einheit von Sichtbarkeit und Version. Jedes Konto hat einen persönlichen Bereich, jede Einkaufsliste ist ein geteilter Bereich. |
| Datensatz | `recordId`, Eintrag in `records` | abgeglichener Eintrag mit einer ID, die meist auf dem Gerät entsteht |
| Kommando | Eintrag in `commands` | Änderung eines Geräts an genau einem Datensatz: anlegen, Felder setzen oder löschen |
| Warteschlange | – | Kommandos, die auf dem Gerät auf den Server warten |
| Abgleichsrunde | – | eine Anfrage samt dem Einarbeiten ihrer Antwort |
| Version | nur im `token` | Zähler je Bereich, jede übernommene Änderung erhöht ihn |
| Token | `token` | undurchsichtige Zeichenkette mit dem Stand, den das Gerät kennt |
| Vollständiger Stand | `fullState: true` | alle Datensätze eines Bereichs statt nur der Änderungen |
| Löschmarkierung | Eintrag in `deletedRecords` | ID eines gelöschten Datensatzes, die der Server mindestens 45 Tage behält, solange es den Bereich gibt |
| Mitgliedseintrag | Eintrag in `members` | Person in einem geteilten Bereich mit Mitglieds-ID, Name, Rolle und Zustand |
| Hinweis | – | Meldung auf dem Gerät über ein abgelehntes oder aussortiertes Kommando oder einen entfernten Bereich |

## Aufbau

### Kern

Der Kern ist die Bibliothek `Murmli.Backend.Sync` (ohne ASP.NET Core). Er kennt keine Datentypen: Er verwaltet Bereiche, Mitgliedseinträge, verarbeitete Kommandos, Löschmarkierungen und die Kennung der Datenbank, wendet Kommandos an, liefert Änderungen und räumt abgelaufene Löschmarkierungen und Kommando-IDs auf ([Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)). Den Endpunkt `POST /v1/sync` samt seiner Rate-Limits (`SyncRateLimiter`) und den Hintergrunddienst des Aufräumens (`SyncCleanupService`) stellt die API (`Murmli.Backend.Api/Sync`, [api.md](api.md#abgleich)), Abhängigkeiten laufen `Telemetry ← Data ← Sync ← Api ← Api.Host`. Öffentlich ist nur, was die API braucht: die Typen von Anfrage und Antwort (`Protocol`), `SyncProcessor` mit `SyncCaller`, `SyncResponseEncoding` und `SyncOutcome`, der Vertrag der Datentypen (`DataTypes`), der Server als Schreiber (`SyncServerWriter` mit `SyncServerCommand`, `SyncActor` und `SyncServerWriteResult`, [Server als Schreiber](#server-als-schreiber)), die Beiträge zum ersten Abgleich eines Kontos (`ISyncAccountBootstrapContributor`, [Erster Abgleich eines Kontos](#erster-abgleich-eines-kontos)), das Aufräumen (`SyncCleanupRunner`, `ISyncCleanupContributor`, `SyncCleanupContext`), die Schritte des Abgleichs in der Kontolöschung (`SyncAccountLifecycle` mit `MarkDeletingAsync` und `DeleteOwnedScopesAsync`, [Kontolöschung](#kontolöschung)), `SyncOptions` samt `SyncDataTypeOptions`, `SyncCleanupOptions` und `SyncRateLimitOptions` sowie `AddSync()`, `AddSyncDataType<T>()`, `AddSyncCleanupContributor<T>()` und `AddSyncAccountBootstrapContributor<T>()`; alles andere ist intern. Die Entitäten der Kerntabellen und die Basisklasse `SyncRecord` liegen in `Murmli.Backend.Data/Sync`, ohne `DbSet` am `MurmliDbContext`; der Kern liest und schreibt sie über `context.Set<T>()`.

### Tragende Regel

Abgeglichene Tabellen ändert nur der Kern. Jede Änderung an einem Datensatz, seiner Löschmarkierung oder einem Mitgliedseintrag erhöht die Version ihres Bereichs unter dessen Zeilensperre in derselben Transaktion; was am Kern vorbei schreibt (eigener Code, direktes SQL, Löschweitergabe zwischen Datensätzen), erzeugt Änderungen, die kein Gerät je sieht. Ausnahmen sind das Löschen ganzer Bereiche, mit ihrem Wurzel-Datensatz oder mit dem Konto (Geräte erkennen den fehlenden Bereich über ihr Token), sowie der verarbeiteten Kommandos eines Kontos per `ON DELETE CASCADE` von `identity.users`; Mitgliedseinträge eines gelöschten Kontos in fremden Bereichen verlieren per `ON DELETE SET NULL` nur die Verbindung zum Konto ([#131](https://github.com/Murmli/MurmliV2/issues/131)). Das Aufräumen entfernt abgelaufene Löschmarkierungen ohne neue Version und hebt dafür in derselben Transaktion die Untergrenze ihres Bereichs an ([Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)).

### Verarbeitung einer Anfrage

1. Token lesen; ein unlesbares Token ist kein Fehler.
2. Heads: Eine Anweisung liest den persönlichen Bereich und jeden geteilten Bereich, in dem das Konto einen aktiven Mitgliedseintrag hat (Version, Epoche, Untergrenze), samt Kennung der Datenbank und der Markierung „wird gelöscht“ des Kontos; ist sie gesetzt, endet die Anfrage mit 410 ([Kontolöschung](#kontolöschung)). Sichtbar sind davon der persönliche Bereich und die geteilten, deren Wurzel-Datentyp in `dataTypes` steht ([Bereiche und Mitglieder](#bereiche-und-mitglieder)). Fehlt der persönliche Bereich, legt der Kern ihn in einer eigenen Transaktion an, nur für ein Konto ohne Markierung (`INSERT … SELECT … ON CONFLICT DO NOTHING` auf dem eindeutigen Index je Konto, so entsteht er auch bei gleichzeitigen ersten Abgleichen genau einmal), schreibt in derselben Transaktion, was die Beiträge zum ersten Abgleich vorsehen ([Erster Abgleich eines Kontos](#erster-abgleich-eines-kontos)), und liest die Heads erneut.
3. Ohne Kommandos und mit unverändertem Stand antwortet er mit demselben Token, ohne weitere Anweisung.
4. Jedes Kommando läuft in einer eigenen Transaktion (READ COMMITTED): Die Kommando-ID wird als erste Anweisung für das Konto eingetragen. Das Anlegen eines Wurzel-Datensatzes sperrt danach zuerst den persönlichen Bereich des Kontos und liest unter dieser Sperre mit einer eigenen Anweisung die Markierung „wird gelöscht“ des Kontos (gesetzt: die ganze Anfrage 410) und die Zahl seiner eigenen Bereiche der Art. Dann sperrt der Kern den Bereich des Ziels (`FOR NO KEY UPDATE`); fehlt er beim Anlegen eines Wurzel-Datensatzes, entscheidet die gezählte Zahl über die Grenze eigener Bereiche. Danach prüft der Kern den Zugriff (im persönlichen Bereich über den Besitzer, in geteilten über den Mitgliedseintrag) und die Rolle, wendet die Konfliktregeln an, erhöht bei einer übernommenen Änderung die Version des Bereichs um eins und schreibt Datensatz bzw. Löschmarkierung, die Felder des Servers und das Ergebnis zusammen. Das Löschen eines Wurzel-Datensatzes löscht den Bereich mit einer Anweisung, Datensätze, Löschmarkierungen und Mitgliedseinträge folgen per `ON DELETE CASCADE`.
5. Danach liest er in einer Transaktion mit REPEATABLE READ: Heads (fehlt der persönliche Bereich oder ist das Konto markiert, endet die Anfrage mit 410), gezählte Löschmarkierungen, eigene Löschungen entfernter Bereiche, Datensätze je Bereich und Datentyp, Mitgliedseinträge geteilter Bereiche, mitgelieferter Stand, neues Token. Die Antwort schreibt er dabei Block für Block in einen Puffer, bei Bedarf mit gzip ([Kompression](#kompression)).

Warum kein Abruf etwas verpasst: Jede Änderung erhöht die Version unter der Sperre ihres Bereichs, und Postgres macht einen Commit sichtbar, bevor es dessen Sperren freigibt. Wer Version 17 sieht, sieht alle Änderungen bis 17. Ein Abruf „geändert seit Zeitpunkt X“ könnte dagegen Änderungen dauerhaft übersehen, weil Transaktionen nicht in der Reihenfolge ihrer Zeitstempel festgeschrieben werden. Transaktionen, Sperren und eigenes SQL im Einzelnen: [database.md](database.md#abgleich).

### Datentypen

Ein Datentyp ist eine Klasse, die von `SyncDataType<TRecord>` erbt (`Murmli.Backend.Sync.DataTypes`); `TRecord` ist seine Entität und erbt von `SyncRecord` (`Id`, `ScopeId`, `Version`). In `Define` nennt er:

- Namen und Art des Bereichs (`Named`) und die Höchstzahl an Datensätzen je Bereich (`MaximumRecordsPerScope`, Pflicht),
- als Wurzel eines geteilten Bereichs `ScopeRoot(maximumOwnedScopesPerAccount)` mit der Höchstzahl eigener Bereiche dieser Art je Konto; jede geteilte Art hat genau einen Wurzel-Datentyp, der persönliche Bereich keinen,
- seine Felder über Ausdrücke auf die Eigenschaften der Entität (`note => note.Title`): `Field` für ein Feld, das seine eigene Gruppe bildet, `Group` für Felder mit gemeinsamem Zeitpunkt der letzten Änderung, `CreateOnly` für Felder, die nur bei `create` gelten (ohne Zeitpunkt; bei einem `create` auf eine vorhandene ID bleiben sie, ein `setFields` mit ihnen ist `invalid`), `ServerField` für Felder, die nur der Server setzt (mit `isSentToDevices: false` nie an Geräte),
- mit `RequiredOnCreate()` die Pflichtfelder bei `create`,
- die Wertart je Feld aus `SyncFieldTypes`: `Text` (optional mit einem Muster als `Regex`, das den ganzen Text prüfen muss, etwa `\A[a-z]{2,3}\z`), `Key` (Schlüssel aus einer festen Menge, nie `null`), `NullableKey`, `Decimal`, `Boolean`, `Timestamp` (Zeitpunkt) und `MemberReference` (Verweis auf einen Mitgliedseintrag, nur für Felder des Servers),
- bei einem Feld des Servers, womit der Server es füllt (`SyncServerFill`): `CommandTimeOnCreate` mit `time` des `create`, `CommandTimeOnApply` mit `time` jeder übernommenen Änderung, auch beim Anlegen, `ActingMemberOnCreate` mit dem Mitgliedseintrag der Person, die anlegt, `ActingMemberOnApply` mit dem Mitgliedseintrag der Person jeder übernommenen Änderung, auch beim Anlegen; im persönlichen Bereich sind Verweise auf Personen `null`. Ohne Füllung setzen es Regeln des Datentyps oder, außer bei Verweisen auf Personen, der Server als Schreiber ([Server als Schreiber](#server-als-schreiber)).

`MinimumRole(kind)` nennt die Rolle, die ein Kommando dieser Art in einem geteilten Bereich mindestens braucht, Standard `member`; eine Rolle, die der Kern nicht kennt, erlaubt nichts. Den Wurzel-Datensatz löscht unabhängig davon nur der Besitzer. `Evaluate` kann ein Kommando ablehnen (`SyncRuleDecision.Reject`) oder ignorieren (`SyncRuleDecision.Ignore`); es läuft, nachdem die gewonnenen Gruppen am Datensatz stehen und bevor der Server seine Felder füllt, nicht beim Löschen eines Wurzel-Datensatzes, und kennt mit `ActingMemberId` den Mitgliedseintrag der handelnden Person (im persönlichen Bereich `null`). Mit `Applied(feld)` erfährt eine Regel, ob das Kommando den Wert des Felds übernommen hat (seine Gruppe hat gewonnen oder, bei einem Schreiben des Servers, das genannte Feld des Servers; beim Anlegen jedes Feld einer Gruppe und jedes Feld, das nur beim Anlegen gilt; nie bei Feldern mit Füllung, die der Server erst nach den Regeln füllt), mit `Original(feld)` den Wert vor dem Kommando (beim Anlegen den Standard des Typs) und mit `ServerTime` die Uhr des Servers. Werte setzt sie direkt an `Record`, auch an Eigenschaften, die die Definition nicht nennt; solche internen Spalten (etwa `checked_arrived_at` der Einkaufsliste) gehen nie an Geräte. Registriert wird ein Datentyp mit `services.AddSyncDataType<T>()`. Beim Start prüft der Kern jede Registrierung, auch gegen das EF-Modell (Entität und Eigenschaften gemappt, gleiche Nullbarkeit, Zeitpunkte nicht nullbar, Füllungen passend zum Typ der Eigenschaft), dazu genau eine Wurzel je geteilter Art und dass `MinimumRole(Create)` mindestens die Rolle von `MinimumRole(SetFields)` verlangt (ein `create` auf eine vorhandene ID wirkt wie `setFields`), und bricht den Start bei einem Fehler ab, nie erst eine Anfrage.

### Server als Schreiber

Einzelne Datensätze eines Datentyps, die der Server selbst anlegt, ändert oder löscht, schreibt er mit `SyncServerWriter.WriteAsync` (`Murmli.Backend.Sync.Writing`, scoped) als Kommando des Servers (`SyncServerCommand`): Kommando-ID, Handelnder (`SyncActor`), Art, Bereich, Datentyp, Datensatz, Felder als `JsonElement` und optional eine Bedingung. IDs von Bereich und Datensatz sind wie beim Gerät zufällige UUIDs der Version 4. Das Kommando läuft in einer eigenen Transaktion durch dieselben Schritte wie das eines Geräts ([Konfliktregeln](#konfliktregeln)), mit der Uhr des Servers als `time`; Geräte erfahren die Änderung wie jede andere. Ausnahme sind die Kommandos der Beiträge zum ersten Abgleich eines Kontos: Sie laufen in der Transaktion, die den persönlichen Bereich anlegt, und liefern kein Ergebnis ([Erster Abgleich eines Kontos](#erster-abgleich-eines-kontos)).

- `SyncActor.OnBehalfOf(accountId)` schreibt im Namen eines Kontos: Die Kommando-ID gilt für dieses Konto, Verweise auf Personen und `ActingMemberId` der Regeln setzt der Server auf dessen Mitgliedseintrag im Bereich, gleich in welchem Zustand (ohne Eintrag und im persönlichen Bereich `null`). Nur so legt der Server einen Wurzel-Datensatz samt Bereich an; für ein Konto ohne persönlichen Bereich oder mit der Markierung „wird gelöscht“ ist das Anlegen `ignored` mit `targetDeleted`. Nur im Namen des Besitzers löscht der Server einen Wurzel-Datensatz oder übernimmt das Anlegen eines Wurzel-Datensatzes, dessen Bereich schon besteht, als Änderung (Schritt 3), sonst ist das Ergebnis `rejected` mit `noAccess`; die Geräte des Besitzers erfahren das Löschen wie das eines eigenen Geräts (`removedByOwnAccount: true`).
- `SyncActor.System` schreibt ohne Person: Die Kommando-ID gilt für den Besitzer des Bereichs, den der Server in derselben Transaktion liest. Fehlt der Bereich oder verschwindet er, bevor der Server ihn sperrt, ist das Ergebnis `ignored` mit `targetDeleted`; fehlt er schon beim Lesen des Besitzers, speichert der Server nichts. Beim Anlegen bleiben Verweise auf Personen `null`, beim Ändern bleiben Felder mit den Füllungen `ActingMemberOnApply` und `CommandTimeOnApply` stehen. Regeln sehen dabei `ActingMemberId` `null`. Einen Wurzel-Datensatz legt `System` nie an und löscht ihn nie (`rejected` mit `invalid`).
- Prüfungen der Uhr, der Mitgliedschaft und der Rolle sowie die Rate-Limits entfallen. Felder des Servers ohne Füllung darf er setzen: Sie gelten in Schritt 13 ohne Vergleich der Zeit und reichen allein, damit das Kommando dort übernommen wird. Felder mit Füllung und Verweise auf Personen darf ein Kommando des Servers nicht nennen (`rejected` mit `invalid`).
- Die Bedingung (`Precondition`) prüft der Kern unter der Sperre des Bereichs am vorhandenen Datensatz, bevor er Werte übernimmt; ist sie nicht erfüllt, ist das Ergebnis `ignored` mit `lostToNewer` (Schritt 14). Für einen neuen Datensatz gilt sie nicht, zusammen mit dem Löschen eines Wurzel-Datensatzes ist sie `invalid`.
- `SyncServerWriteResult` trägt `Status`, `Reason` und `Cause` mit den Werten aus [Ergebnisse](#ergebnisse). Kennt der Server die Kommando-ID schon, liefert er das gespeicherte Ergebnis, bei `System` nur, solange der Bereich besteht (danach `ignored` mit `targetDeleted`); wer nach einer Störung wiederholt, nimmt deshalb dieselbe Kommando-ID. Was der Server vor der Verarbeitung als ungültig erkennt, ist `rejected` mit `invalid` und wird nicht gespeichert: unbekannter Datentyp, unbekannte Art, ein Handelnder weder `System` noch `OnBehalfOf`, IDs von Bereich oder Datensatz keine UUID v4, die ID eines Wurzel-Datensatzes ungleich oder die eines anderen Datensatzes gleich der Bereichs-ID, Felder, die nicht passen, Anlegen oder Löschen eines Wurzel-Datensatzes als `System` und eine Bedingung beim Löschen eines Wurzel-Datensatzes. Ein `invalid` erst in der Verarbeitung, etwa weil der Datentyp nicht zur Art des Bereichs passt (Schritt 7), speichert der Server wie bei einem Gerät. Wird das Konto während des Schreibens gelöscht, ist das Ergebnis `ignored` mit `targetDeleted`, nie eine Exception. Wirft eine Regel oder die Bedingung, ist es `failed`, nichts ist gespeichert, und der Server loggt `SyncCommandFailed` ([telemetry.md](telemetry.md#abgleich)). Eine Störung der Datenbank, auch eine Sperre über `Sync:LockTimeout` hinaus, und ein Abbruch kommen als Exception beim Aufrufer an.

### Erster Abgleich eines Kontos

Was jedes neue Konto beim ersten Abgleich bekommen soll, etwa die erste Einkaufsliste, hängt ein Datentyp mit einer Klasse ein, die `ISyncAccountBootstrapContributor` (`Murmli.Backend.Sync.Accounts`) umsetzt und mit `services.AddSyncAccountBootstrapContributor<T>()` registriert ist (scoped). `CreateCommands(accountId)` liefert Kommandos des Servers mit festen IDs. Der Kern holt sie einmal, bevor er den persönlichen Bereich anlegt, und schreibt sie in derselben Transaktion wie diesen über den Server als Schreiber ([Server als Schreiber](#server-als-schreiber)). Ergebnisse liefert das Schreiben dort nicht: Ein Kommando, das der Server schon vor der Verarbeitung als ungültig erkennt, ist ein Fehler im Code und wirft eine `InvalidOperationException`. Jedes andere Ergebnis außer `applied` (etwa `invalid`, weil der Datentyp nicht zur Art des Bereichs passt, `noAccess` oder `limitExceeded`, [Konfliktregeln](#konfliktregeln)) speichert der Kern wie sonst, ohne dass der Beitrag davon erfährt; das Konto bekommt den Datensatz dann nie. Jede Exception rollt die ganze Transaktion zurück. Eine vorübergehende Störung wiederholt die Execution Strategy noch in derselben Anfrage mit denselben Kommandos; bleibt die Störung, antwortet der Abgleich wie bei jeder Störung beim Anlegen des persönlichen Bereichs mit 503. Wird das Konto gerade gelöscht, antwortet er mit 410, jede andere Exception lässt die Anfrage scheitern. Nach 503 und nach einem Fehler im Code versucht der nächste Abgleich beides wieder. Weil alles in einer Transaktion läuft, schreiben die Kommandos nur in den persönlichen Bereich und in Bereiche, die sie selbst anlegen. Entsteht der persönliche Bereich nicht, weil es ihn schon gibt (auch bei gleichzeitigen ersten Abgleichen und bei der Wiederholung nach einem unklaren Commit), schreibt der Kern sie nicht. Konten, deren persönlicher Bereich vor einem Beitrag entstanden ist, bekommen von ihm deshalb nie etwas; ein Beitrag, den auch sie brauchen, braucht einen eigenen Weg.

### Datentyp anbinden

Vorlage ist die [Einkaufsliste](#einkaufsliste).

1. Entität in `Murmli.Backend.Data/<Bereich>/`, abgeleitet von `SyncRecord`, ohne `required`-Member (der Kern legt Datensätze mit `new()` an), mit einer `IEntityTypeConfiguration` (Vorlage: `ShoppingListItem` und `ShoppingListItemConfiguration`): `ToTable(name, schema)`, danach `MapSyncRecord(name)` für `id` (mit `ValueGeneratedNever()`), `scope_id` mit Fremdschlüssel `ON DELETE CASCADE` auf `sync.scopes` und `version` mit Index `(scope_id, version)`; je Feldgruppe `<gruppe>_changed_at`, Spalten in snake_case wie die Feldnamen im Vertrag. Verweise auf Personen als `<feld>_member_id` (etwa `changed_by_member_id` für `changedBy`) vom Typ `uuid NULL` (`Guid?`) mit Fremdschlüssel ohne Löschweitergabe auf `sync.member_entries` (in EF `DeleteBehavior.ClientNoAction`) und einem partiellen Index `WHERE … IS NOT NULL`; die Tabelle eines Wurzel-Datentyps zusätzlich mit `CHECK (scope_id = id)` (`HasCheckConstraint`, Vorlage `ShoppingListConfiguration`). Interne Spalten, die nur Regeln setzen (etwa `checked_arrived_at`), sind Eigenschaften ohne Feld in der Definition. Kein `DbSet`. Migration erzeugen ([AGENTS.md](../AGENTS.md#datenbank)).
2. Datentyp-Klasse im Ordner des Bereichs in `Murmli.Backend.Api`, intern, mit `Define` und bei Bedarf `Evaluate` (Vorlage: `ShoppingListType`, `ShoppingListItemType`); Konstanten heißen `DataTypeName` und `ScopeKindName`, denn `Name` und `ScopeKind` verdeckten die Eigenschaften von `SyncDataType`. `AddSyncDataType<T>()` in `AddApi()`.
3. Feste Schlüssel als `Key` oder `NullableKey` aus einer statischen Klasse (Vorlage `ShoppingCatalog`), die eine Datei unter `src/sync-cases/<bereich>/` spiegelt, damit App und Server dieselben lesen; ein Test nimmt jeden Schlüssel der Datei an.
4. Bei Bedarf ein Beitrag zum ersten Abgleich ([Erster Abgleich eines Kontos](#erster-abgleich-eines-kontos), Vorlage `FirstShoppingListContributor`), ein Beitrag zum Aufräumen ([Beiträge der Datentypen](#beiträge-der-datentypen), Vorlage `CheckedItemsCleanupContributor`) und eigene Optionen (Vorlage `ShoppingListOptions`).
5. Felder, Gruppen, Regeln und Grenzen in diesem Dokument beschreiben, wie bei der [Einkaufsliste](#einkaufsliste).
6. Fälle zu den Regeln des Datentyps in Unterordnern je Thema unter `src/sync-cases/<bereich>/` ([Fallsammlung](#fallsammlung)); einen neuen Ordner zusätzlich in `CaseAreas` von `SyncEndpointsCaseCollectionTests` aufnehmen (derzeit `core` und `shopping`).
7. Ein Test, der jedes Feld über `/v1/sync` anlegt und zurückliest (Vorlage `ShoppingListItemTypeTests.Sync_ItemWithEveryField_RoundTripsEveryFieldExceptCreator`).
8. Die Seite der App nach [#124](https://github.com/Murmli/MurmliV2/issues/124).

Kern und Endpunkt ändern sich dafür nicht.

## Vertrag

### Kennungen

| Kennung | Vergibt | Regel |
| --- | --- | --- |
| Konto | Server, aus der Anmeldung | steht nie im Body einer Anfrage und in keiner Antwort |
| Gerät (`Murmli-Device-Id`) | App, je Installation und Anmeldung | endet mit der Abmeldung; dient der Grenze je Gerät und der Telemetrie, später dem Live-Kanal ([#125](https://github.com/Murmli/MurmliV2/issues/125)) und Push |
| Bereich (`scopeId`) | App für Bereiche, die auf dem Gerät entstehen; Server für den persönlichen Bereich und Bereiche, die er selbst anlegt | bei einem geteilten Bereich zugleich die ID seines Wurzel-Datensatzes, bei Listen also die ID der Liste |
| Datensatz (`recordId`, in Antworten `id`) | wer ihn anlegt, meist die App | gibt es je Datentyp nur einmal, über alle Bereiche hinweg |
| Kommando (`id`, in Ergebnissen `commandId`) | App | gilt je Konto, ein fremdes Konto kann eine ID nicht vorab belegen; der Server führt jede ID höchstens einmal aus |
| Mitglied (`memberId`, Verweise wie `changedBy`) | Server | Kennung einer Person innerhalb eines Bereichs anstelle der Konto-ID |
| Datentyp, Kommandoart | fest im Code | Namen in camelCase, etwa `shoppingListItem`; Kommandoarten `create`, `setFields`, `delete` |

Version und Token vergibt der Server ([Token](#token)).

Alle IDs, die ein Gerät vergibt, sind zufällige UUIDs der Version 4 aus einer kryptografisch sicheren Quelle. Zeitlich sortierte Varianten sind ausgeschlossen: Mitglieder eines Bereichs sehen die IDs der anderen und könnten deren nächste IDs sonst vorhersagen und belegen. Eine UUID anderer Version macht ein Kommando `invalid`, als Geräte-ID die ganze Anfrage zur 400.

### Bereiche und Mitglieder

Jedes Konto hat genau einen persönlichen Bereich (`kind: "personal"`) für private Daten. Der Server legt ihn beim ersten Abgleich des Kontos an, mit ihm die erste Einkaufsliste ([Einkaufsliste](#einkaufsliste)); Geräte erfahren beides nur über den Abgleich; bis zum ersten abgeschlossenen Abgleich nach der Anmeldung nimmt die App keine Kommandos an ([#124](https://github.com/Murmli/MurmliV2/issues/124)).

Ein geteilter Bereich, bei Einkaufslisten `kind: "shoppingList"`, entsteht nur mit dem `create` seines Wurzel-Datensatzes, bei Listen der Liste selbst. Seine ID ist die des Wurzel-Datensatzes, der Absender wird sein Besitzer. Jedes andere Kommando in einen Bereich, den es nicht gibt, ist `noAccess` und legt nichts an. Den Wurzel-Datensatz löscht nur der Besitzer, danach ist der Bereich mit allen Datensätzen und Mitgliedseinträgen für alle weg. Mitgliedschaft ändert kein Kommando, sie ändert sich nur online ([#129](https://github.com/Murmli/MurmliV2/issues/129)).

Sichtbar sind einem Konto sein persönlicher Bereich und jeder geteilte Bereich, in dem es einen aktiven Mitgliedseintrag hat und dessen Wurzel-Datentyp in `dataTypes` steht. Geteilte Bereiche, deren Wurzel-Datentyp nicht in `dataTypes` steht, schickt der Server nicht, nimmt sie nicht ins Token auf und meldet sie nicht als entfernt: Eine alte App sieht nie einen Bereich, den sie nicht darstellen kann, eine aktualisierte verwirft ihr Token und bekommt ihn mit dem vollständigen Stand.

Der Block eines geteilten Bereichs nennt in `members` immer alle Mitgliedseinträge, der persönliche Bereich hat keine. Ein Eintrag ist `{ memberId, name, role, state, isOwn }`, `isOwn` kennzeichnet den eigenen. Datensätze nennen Personen nur über die Mitglieds-ID, etwa in `changedBy`; Konto-IDs stehen in keiner Antwort. `name` ist leer bis zum Anzeigenamen aus [#146](https://github.com/Murmli/MurmliV2/issues/146). Bis zum Teilen ([#143](https://github.com/Murmli/MurmliV2/issues/143)) hat jeder geteilte Bereich nur den Eintrag seines Besitzers (`role: "owner"`, `state: "active"`).

### Anfrage

`POST /v1/sync`, nur mit Anmeldung. Das OpenAPI-Dokument beschreibt den Endpunkt, Kommandos und `fields` aber nur als freie Objekte; die Felder je Datentyp stehen in diesem Dokument ([Einkaufsliste](#einkaufsliste)).

| Header | Regel |
| --- | --- |
| `Authorization: Bearer <JWT>` | wie bei jedem Endpunkt |
| `Murmli-Device-Id` | Pflicht, UUID der Version 4 in der Form mit Bindestrichen (`6b0f3c2e-7a43-4c4f-9d0b-2f1c9d6a8e11`) |
| `Content-Type: application/json` | Pflicht, sonst 415 |
| `Accept-Encoding: gzip` | optional, erlaubt gzip für vollständige Stände ([Kompression](#kompression)); dart:io sendet ihn immer |

Einen Header für die App-Version gibt es nicht ([Reserviert](#reserviert)).

Body:

| Feld | Typ | Pflicht | Bedeutung |
| --- | --- | --- | --- |
| `token` | String oder `null` | nein | Token der letzten Antwort; fehlt beim ersten Abgleich und für eine Reparatur |
| `dataTypes` | Array von Strings | ja | Datentypen, die diese Version der App versteht |
| `commands` | Array | ja, darf leer sein | wartende Kommandos in der Reihenfolge ihrer Entstehung, höchstens `settings.maxCommandsPerRequest`, der ganze UTF-8-Body höchstens `settings.maxRequestBytes` Byte ([Vorgaben](#vorgaben)) |
| `pendingCommandCount` | ganze Zahl | nein | Länge der Warteschlange beim Senden, samt den Kommandos dieser Anfrage (Telemetrie) |
| `oldestPendingCommandAgeSeconds` | ganze Zahl | nein | Alter des ältesten wartenden Kommandos in Sekunden nach der korrigierten Uhr des Geräts (Telemetrie) |

Unbekannte Felder im Rahmen der Anfrage ignoriert der Server.

Entschieden, noch nicht gebaut: die Auswertung von `pendingCommandCount` und `oldestPendingCommandAgeSeconds` in der Telemetrie; bis dahin nimmt der Server sie nur an. Entfällt, sobald die Telemetrie des Abgleichs aus [#138](https://github.com/Murmli/MurmliV2/issues/138) gebaut ist.

Kommando:

| Feld | Typ | Regel |
| --- | --- | --- |
| `id` | UUID v4 | Kommando-ID, eindeutig je Konto |
| `kind` | String | `create`, `setFields` oder `delete` |
| `scopeId` | UUID v4 | Bereich des Ziels |
| `dataType` | String | Datentyp, den der Server kennt und der in `dataTypes` steht |
| `recordId` | UUID v4 | beim Wurzel-Datentyp eines geteilten Bereichs gleich `scopeId`, bei jedem anderen Datentyp verschieden |
| `time` | ganze Zahl | Zeitpunkt der Aktion in Unix-Millisekunden nach der korrigierten Uhr des Geräts ([Zeit](#zeit)), von `-62135596800000` bis `253402300799999`; ein Wert in Mikrosekunden liegt darüber und ist `invalid` |
| `fields` | Objekt | nur bei `create` und `setFields`, bei `setFields` nicht leer; bei `delete` fehlt es oder ist `null` |

Ein Kommando mit einem unbekannten Feld, einem doppelten Feldnamen (auch in `fields`) oder einem fehlerhaften Wert lehnt der Server als `invalid` ab; die anderen Kommandos der Anfrage berührt das nicht.

Regeln für `fields`:

- Ein Datentyp verbindet Felder zu Gruppen, die nur gemeinsam gewinnen oder verlieren, etwa Menge und Einheit; ein einzelnes Feld ist seine eigene Gruppe. Nennt ein Kommando ein Feld einer Gruppe, nennt es alle.
- `setFields` trägt nur die Gruppen, die sich geändert haben. Eine ältere App kann so ein Feld, das sie nicht kennt, nie überschreiben.
- Felder, die nur beim Anlegen gelten, nimmt der Server nur mit `create` an.
- Felder des Servers (etwa `createdAt`, `changedAt`, `changedBy`) setzt nur der Server. Nennt ein Kommando eines, ist es `invalid`; der Server übernimmt so nie einen gesendeten Verweis auf eine Person.
- Fehlt ein Feld bei `create`, gilt sein Standard: `null`, wo erlaubt, ein leerer Text bei Mindestlänge 0, bei Wahrheitswerten deren Standard. Pflichtfelder müssen dabei sein.

| Wertart | JSON | Prüfung |
| --- | --- | --- |
| Text | String | Länge in UTF-16-Einheiten (wie `String.length` in Dart), kein U+0000, keine einzelnen Surrogates, je Feld eventuell ein Muster; der Server speichert den Text genau so |
| Zahl | Zahl | Wertebereich und höchstens erlaubte Nachkommastellen |
| Wahrheitswert | `true` oder `false` | – |
| Schlüssel | String | aus einer festen Menge, etwa dem Katalog der [Einkaufsliste](#einkaufsliste) |
| Zeitpunkt | ganze Zahl | Unix-Millisekunden im Wertebereich von `time` |

`null` nur, wo der Datentyp es erlaubt. Jeder Verstoß, ein unbekannter oder doppelter Feldname, eine unvollständige Gruppe, ein leeres `setFields`, `fields` bei `delete` oder ein fehlendes Pflichtfeld bei `create` macht nur dieses Kommando `invalid`.

```http
POST /v1/sync HTTP/1.1
Authorization: Bearer eyJhbGciOi...
Murmli-Device-Id: 6b0f3c2e-7a43-4c4f-9d0b-2f1c9d6a8e11
Content-Type: application/json
Accept-Encoding: gzip

{
  "token": "AY7M2xR...Ug",
  "dataTypes": ["shoppingList", "shoppingListItem"],
  "pendingCommandCount": 3,
  "oldestPendingCommandAgeSeconds": 42,
  "commands": [
    {
      "id": "5b2b1a0e-6a4f-4c43-9a4b-2a3f0c7e9d11",
      "kind": "create",
      "scopeId": "0f5c6b4e-2d7a-4f0b-8a51-6a0d3f3e2c90",
      "dataType": "shoppingListItem",
      "recordId": "c1e2d3f4-0a1b-4c2d-8e3f-405162738495",
      "time": 1795400000123,
      "fields": { "name": "Hafermilch", "quantity": 1, "unit": "liter", "category": "dairy", "checked": false, "language": "de" }
    },
    {
      "id": "8d7c6b5a-4f3e-4d2c-9b1a-0f1e2d3c4b5a",
      "kind": "setFields",
      "scopeId": "0f5c6b4e-2d7a-4f0b-8a51-6a0d3f3e2c90",
      "dataType": "shoppingListItem",
      "recordId": "c1e2d3f4-0a1b-4c2d-8e3f-405162738495",
      "time": 1795400000456,
      "fields": { "checked": true }
    },
    {
      "id": "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d",
      "kind": "delete",
      "scopeId": "0f5c6b4e-2d7a-4f0b-8a51-6a0d3f3e2c90",
      "dataType": "shoppingListItem",
      "recordId": "aa11bb22-cc33-4d44-8e55-ff6677889900",
      "time": 1795400000789
    }
  ]
}
```

### Antwort

Status 200 mit `Content-Type: application/json; charset=utf-8`, auch wenn einzelne Kommandos scheitern.

| Feld | Inhalt |
| --- | --- |
| `results` | ein Ergebnis je verarbeitetem Kommando, nach Position: `results[i]` gehört zu `commands[i]`. Immer ein Anfang von `commands`; endet früher nach `failed` oder wenn die Zeit für Kommandos einer Anfrage aufgebraucht ist (20 s, das erste Kommando läuft immer) |
| `scopes` | ein Block je Bereich mit Neuigkeiten: `scopeId`, `kind`, `fullState`, `records`, `deletedRecords`, `members` |
| `removedScopes` | Bereiche, die das Gerät entfernen muss: `{ scopeId, removedByOwnAccount }` ([Entfernte Bereiche](#entfernte-bereiche)) |
| `coversAllScopes` | `true` genau dann, wenn die Anfrage kein lesbares Token hatte oder eines mit fremder Kennung der Datenbank |
| `token` | neues Token |
| `hasMore` | derzeit immer `false` ([Reserviert](#reserviert)) |
| `serverTime` | Uhrzeit des Servers in Unix-Millisekunden, gelesen, wenn die Antwort fertig ist ([Zeit](#zeit)) |
| `settings` | Vorgaben an das Gerät ([Vorgaben](#vorgaben)) |

- Jedes Feld des Rahmens steht immer in der Antwort, auch leer oder `false`. Eine Antwort ist ein einziges JSON-Dokument, ein abgeschnittener Body lässt sich so nie lesen.
- Ein Ergebnis ist `{ commandId, status }`, dazu `reason` nur bei `rejected`, `cause` nur bei `ignored` und `original` (`{ status, reason?, cause? }`) nur bei `alreadyProcessed`. `commandId` ist `null`, wenn das Kommando keine gültige ID hatte.
- Ein Datensatz ist `{ dataType, id, fields }`, ein eigenes Objekt, damit das Gerät einen einzelnen unlesbaren überspringen kann. `fields` trägt den vollständigen aktuellen Stand: alle Felder, die Geräte sehen, samt der Felder des Servers wie `createdAt` und `changedAt`, nie Versionen, Zeitpunkte je Gruppe oder Felder, die der Server nicht an Geräte gibt.
- Ein Eintrag in `deletedRecords` ist `{ dataType, id }` und gilt nur für den Datensatz mit dieser ID in diesem Bereich.
- Innerhalb eines Blocks steht jeder Datensatz höchstens einmal, nie zugleich als Datensatz und als Löschung.
- Reihenfolge: erst der persönliche Bereich, dann aufsteigend nach Bereichs-ID; Datensätze nach Datentyp in einer festen Reihenfolge des Servers, dann nach ID.

Das Gerät prüft zuerst den Rahmen: Ergebnisse, Token, Kennzeichen und Aufbau. Passt er nicht, ist die Antwort unlesbar und das Gerät wendet nichts an; einen einzelnen Datensatz, den es nicht lesen kann, überspringt es und meldet ihn. Dann arbeitet es die Antwort in einer einzigen lokalen Transaktion ein ([#124](https://github.com/Murmli/MurmliV2/issues/124)):

1. Prüfen, dass Token und Stand-Zähler (steigt mit jedem Löschen der lokalen Daten und jedem Verwerfen des Tokens) noch die der Anfrage sind, sonst die Antwort verwerfen.
2. Bei `coversAllScopes: true` jeden lokalen Bereich entfernen, den die Antwort nicht enthält.
3. Die Bereiche aus `removedScopes` mit ihren Datensätzen, Mitgliedern und wartenden Kommandos löschen.
4. Bei `fullState: true` den lokalen Stand des Bereichs durch `records` ersetzen, sonst `records` übernehmen und `deletedRecords` entfernen; `members` ersetzt die Mitgliedseinträge des Bereichs.
5. Kommandos mit Ergebnis aus der Warteschlange entfernen, außer bei `failed` und `accepted`; Kommandos ohne Ergebnis bleiben.
6. Alle noch wartenden Kommandos in ihrer Reihenfolge erneut auf die lokalen Daten anwenden, auch solche, die während der Runde entstanden sind. So überschreibt der Stand des Servers nie eine eigene Änderung, die er noch nicht kennt.
7. Token, Abweichung der Uhr und Vorgaben speichern.

Meldet die Antwort `hasMore` oder hat die Runde mindestens ein Kommando entfernt, während noch welche warten, folgt die nächste Runde sofort.

```json
{
  "results": [
    { "commandId": "5b2b1a0e-6a4f-4c43-9a4b-2a3f0c7e9d11", "status": "alreadyProcessed", "original": { "status": "applied" } },
    { "commandId": "8d7c6b5a-4f3e-4d2c-9b1a-0f1e2d3c4b5a", "status": "ignored", "cause": "lostToNewer" },
    { "commandId": "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "status": "ignored", "cause": "targetDeleted" }
  ],
  "scopes": [
    {
      "scopeId": "0f5c6b4e-2d7a-4f0b-8a51-6a0d3f3e2c90",
      "kind": "shoppingList",
      "fullState": false,
      "records": [
        {
          "dataType": "shoppingListItem",
          "id": "c1e2d3f4-0a1b-4c2d-8e3f-405162738495",
          "fields": { "name": "Hafermilch", "quantity": 1, "unit": "liter", "category": "dairy", "checked": false, "language": "de", "createdAt": 1795400000123, "changedAt": 1795400000600, "changedBy": "6e1c0b8a-3f2d-4a5e-9b7c-8d9e0f1a2b3c" }
        }
      ],
      "deletedRecords": [ { "dataType": "shoppingListItem", "id": "aa11bb22-cc33-4d44-8e55-ff6677889900" } ],
      "members": [ { "memberId": "6e1c0b8a-3f2d-4a5e-9b7c-8d9e0f1a2b3c", "name": "", "role": "owner", "state": "active", "isOwn": true } ]
    }
  ],
  "removedScopes": [ { "scopeId": "77aa66bb-55cc-4d44-9e33-ff2211009988", "removedByOwnAccount": true } ],
  "coversAllScopes": false,
  "token": "AY7M2xR...Gw",
  "hasMore": false,
  "serverTime": 1795400043000,
  "settings": { "pollIntervalSeconds": 60, "maxCommandsPerRequest": 500, "maxRequestBytes": 1000000 }
}
```

Der häufigste Fall, ein Abruf ohne Änderungen, wiederholt das Token der Anfrage, hier das der Antwort oben:

```json
{ "results": [], "scopes": [], "removedScopes": [], "coversAllScopes": false, "token": "AY7M2xR...Gw", "hasMore": false, "serverTime": 1795400103000, "settings": { "pollIntervalSeconds": 60, "maxCommandsPerRequest": 500, "maxRequestBytes": 1000000 } }
```

### Ergebnisse

| `status` | Bedeutung | Gerät |
| --- | --- | --- |
| `applied` | Der Datensatz wurde angelegt oder gelöscht, oder mindestens eine Gruppe wurde übernommen. | Kommando aus der Warteschlange entfernen |
| `ignored` mit `cause` | Das Kommando hat gegen einen neueren Stand verloren (`lostToNewer`), oder sein Ziel ist gelöscht oder fehlt (`targetDeleted`). | entfernen, ohne Hinweis |
| `rejected` mit `reason` | Das Kommando war nicht erlaubt oder ungültig. | entfernen und einen Hinweis mit Ziel, Art und Grund ablegen |
| `alreadyProcessed` mit `original` | Die Kommando-ID kennt der Server schon. `original` trägt das ursprüngliche Ergebnis (nie `failed` oder `alreadyProcessed`), damit ein Hinweis nicht verloren geht, wenn die erste Antwort nie ankam. | wie beim ursprünglichen Ergebnis |
| `failed` | Ein Fehler, der an genau diesem Kommando liegt. Der Server speichert nichts dazu und verarbeitet die folgenden Kommandos dieser Anfrage nicht mehr. | behalten und später erneut senden; scheitert dasselbe Kommando wiederholt, sortiert das Gerät es aus ([#124](https://github.com/Murmli/MurmliV2/issues/124)) |

`accepted` ist reserviert ([Reserviert](#reserviert)). Störungen der Datenbank oder des Dienstes sind nie `failed`, sondern ein [Fehler der ganzen Anfrage](#fehler-der-ganzen-anfrage) mit Wartezeit; sonst sortierten Geräte bei einer längeren Störung gültige Kommandos aus.

| `reason` | Bedeutung |
| --- | --- |
| `noAccess` | kein Zugriff: kein Mitglied, Rolle reicht nicht, Bereich gibt es nicht, oder die ID liegt in einem anderen Bereich (dieselbe Antwort wie ohne Recht, nichts vom fremden Datensatz) |
| `limitExceeded` | Grenze überschritten: Datensätze je Bereich oder eigene Bereiche je Konto ([Grenzen](#grenzen)) |
| `invalid` | ungültig: unbekannte Art, fehlende, unbekannte oder unzulässige Werte, Datentyp nicht in `dataTypes` oder nicht passend zur Art des Bereichs |
| `clockAhead` | Uhr geht vor: Zeitpunkt mehr als 5 Minuten nach der Uhr des Servers |
| `tooOld` | zu alt: Zeitpunkt mehr als 45 Tage vor der Uhr des Servers |
| `accountMayNotWrite` | reserviert: Konto darf nicht schreiben ([#133](https://github.com/Murmli/MurmliV2/issues/133)) |

Ein Gerät behandelt einen unbekannten Grund wie `invalid` und eine unbekannte Ursache wie `lostToNewer`. Regeln eines Datentyps, die ein Kommando ignorieren, nutzen `lostToNewer` (still zusammenführen), Regeln, die ablehnen, einen vorhandenen Grund. Treffen bei der Prüfung vor der Verarbeitung mehrere Gründe zu, gewinnt `invalid` vor `tooOld` vor `clockAhead`.

- Der Server verarbeitet jedes Kommando für sich, in der Reihenfolge der Anfrage. Die Anfrage als Ganzes ist nicht atomar: Verarbeitete Kommandos bleiben verarbeitet, auch wenn die Anfrage danach scheitert, und kommen beim erneuten Senden als `alreadyProcessed` zurück.
- `alreadyProcessed` trägt immer das gespeicherte Ergebnis, auch wenn der Server eine Verarbeitung nach einem unklaren Commit wiederholt oder dieselbe Anfrage zweimal gleichzeitig ankommt. Ein spät erneut gesendetes Kommando, das schon verarbeitet ist, wird so nie `tooOld`, solange der Server seine Kommando-ID kennt; ist sie aufgeräumt, ist ein übernommenes Kommando ohnehin älter als die Aufbewahrung und wird `tooOld`, ein abgelehntes oder ignoriertes prüft der Server neu ([Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)).
- Fehlt eines der Felder `id`, `kind`, `scopeId`, `dataType`, `recordId` und `time`, lässt es sich nicht lesen (IDs keine UUID v4, `time` keine ganze Zahl im erlaubten Wertebereich) oder kennt der Server Art oder Datentyp nicht, speichert er das Kommando nicht; es bekommt bei jeder Wiederholung wieder `rejected` mit `invalid`.
- Nach `failed` verarbeitet der Server ein erneut gesendetes Kommando neu.
- Zwei Konten können dieselbe Kommando-ID nutzen, ohne sich zu stören.

Hinweise, entschieden am 2026-11-22 wie in [#148](https://github.com/Murmli/MurmliV2/issues/148) vorgeschlagen (offene Punkte 125a und 125b aus [#122](https://github.com/Murmli/MurmliV2/issues/122)): Zu `rejected` zeigt die App einen allgemeinen Hinweis und nennt als einzigen Grund eigens „Uhr geht vor“ (`clockAhead`), später auch „Konto darf nicht schreiben ([#133](https://github.com/Murmli/MurmliV2/issues/133))“. Zu `ignored` gibt es keinen Hinweis, auch nicht, wenn das Ziel gelöscht wurde (`targetDeleted`). Der Vertrag liefert in jedem Fall Grund oder Ursache und das Ziel.

Entschieden, noch nicht gebaut: die Anzeige der Hinweise. Entfällt, sobald die Hinweise aus [#148](https://github.com/Murmli/MurmliV2/issues/148) gebaut sind.

### Zeit

- Alle Zeitpunkte im Vertrag sind ganze Unix-Millisekunden in UTC (`time`, `serverTime`, Felder wie `createdAt`), Dauern ganze Sekunden (`oldestPendingCommandAgeSeconds`, `pollIntervalSeconds`).
- Der Zeitpunkt eines Kommandos ist der Moment der Aktion nach der Uhr des Geräts, korrigiert um die zuletzt bekannte Abweichung zur Uhr des Servers. Korrigiert wird beim Entstehen des Kommandos, nicht beim Senden oder am Server: Die Laufzeit einzelner Anfragen schwankt, eine Korrektur je Anfrage könnte die Reihenfolge zweier schneller Aktionen desselben Geräts vertauschen.
- Abweichung: `serverTime` minus die Uhr des Geräts in dem Moment, in dem die Antwort ankommt. Der Server liest `serverTime`, wenn die Antwort fertig ist (nach der letzten Abfrage), nie zu Beginn der Anfrage und nie aus der Verarbeitung eines Kommandos.
- Kleine Sprünge der Uhr nach hinten gleicht das Gerät aus: Liegt die Uhr bis zu 5 Minuten hinter dem Zeitpunkt seines letzten Kommandos, etwa nach einer automatischen Korrektur, nimmt es dessen Zeitpunkt, damit eine Aktion nicht gegen die eigene vorige verliert. Bei einem größeren Sprung gilt die Uhr, wie sie ist; sonst zöge eine einmal weit vorgestellte Uhr alle späteren Kommandos mit in die Zukunft.
- Der Server kürzt seine Uhr auf ganze Millisekunden und lehnt Zeitpunkte mehr als 5 Minuten in der Zukunft (`clockAhead`) und mehr als 45 Tage in der Vergangenheit (`tooOld`) ab ([Konfliktregeln](#konfliktregeln)).

### Token

- Das Gerät behandelt das Token als undurchsichtig: Es speichert es, schickt es unverändert zurück und liest nie etwas daraus.
- Ein unlesbares Token ist nie ein Fehler, der Server antwortet dann mit dem vollständigen Stand.
- Ist der Stand unverändert, kommt dasselbe Token zurück. Darauf bauen die Prüfung der App auf veraltete Antworten und die Simulation ([#136](https://github.com/Murmli/MurmliV2/issues/136)).
- Das neue Token nennt jeden sichtbaren Bereich mit der Version, bis zu der das Gerät nach dieser Antwort alle Änderungen kennt; ein Abruf verpasst so keine Änderung.
- Das Gerät darf jederzeit ohne Token anfragen, um sich zu reparieren, seine Warteschlange behält es dabei. Es verwirft sein Token etwa nach einem Update, das neue Datentypen oder Felder kennt, und wenn es ein Kommando aussortiert ([#124](https://github.com/Murmli/MurmliV2/issues/124)).
- Das Token ist weder geheim noch signiert: Ein gefälschtes schadet nur dem eigenen Gerät, weil der Server Sichtbarkeit und Mitgliedschaft bei jeder Anfrage selbst prüft.

Aufbau, nur für das Backend: base64url ohne Padding über diese Bytes.

| Bytes | Inhalt |
| --- | --- |
| 1 | Format, `0x01` |
| 16 | Kennung der Datenbank (GUID-Bytes, big-endian) |
| 28 je Bereich, streng aufsteigend nach den Bytes der Bereichs-ID | Bereichs-ID (16, big-endian), Epoche (4, int32 big-endian), Version (8, int64 big-endian) |

Kennung der Datenbank und Epoche zeigen, dass ein Token zu einem anderen Verlauf gehört; der Vergleich der Versionen allein genügt dafür nicht. Die Epoche ist zufällig und entsteht mit dem Bereich. Die Kennung der Datenbank erneuert ein Mensch nach dem Einspielen einer Sicherung, denn danach zählt der Server von einem älteren Stand weiter und überholt die Version im Token irgendwann. Unlesbar ist ein Token mit fehlerhaftem base64url, unbekanntem Format, einer Länge ungleich 17 + 28 × n oder nicht streng aufsteigenden Bereichs-IDs. Größe: 17 + 28 × n Byte, bei den höchstens 85 Bereichen, die das Teilen erlaubt (42 eigene, 42 fremde, der persönliche), 2.397 Byte oder rund 3,2 KB Text je Anfrage und Antwort.

### Vollständiger Stand und Änderungen

Hat die Anfrage keine Kommandos und nennt ihr lesbares Token mit der aktuellen Kennung der Datenbank genau die sichtbaren Bereiche mit unveränderten Epochen und Versionen, enthält die Antwort keine Blöcke und wiederholt das Token. Sonst bekommt ein sichtbarer Bereich den vollständigen Stand, wenn:

| Bedingung | `coversAllScopes` |
| --- | --- |
| kein Token oder ein unlesbares | `true` |
| Kennung der Datenbank im Token weicht ab | `true` |
| das Token nennt den Bereich nicht | `false` |
| die Epoche weicht ab | `false` |
| die Version im Token liegt unter der Untergrenze des Bereichs | `false` |
| die Version im Token liegt über der des Servers | `false` |
| über der Version im Token liegen mehr Löschmarkierungen der Datentypen aus `dataTypes` als die Schwelle: die Summe der Höchstzahlen an Datensätzen je Bereich über die Datentypen der Art | `false` |
| ein `delete` des Wurzel-Datensatzes dieses Bereichs in dieser Anfrage wurde nicht angewendet | `false` |

Sonst gibt eine Version im Token unter der des Servers einen Block mit allem darüber: geänderte Datensätze, Löschungen und bei geteilten Bereichen `members`. Bei gleicher Version gibt es keinen Block, außer der mitgelieferte Stand verlangt einen. Der Server schickt nur Datensätze und Löschungen der Datentypen aus `dataTypes`.

- Mit `coversAllScopes: true` trägt die Antwort jeden sichtbaren Bereich mit vollständigem Stand, auch den leeren persönlichen, und das Gerät entfernt jeden lokalen Bereich, den sie nicht enthält. Ohne diese Regel bliebe ein Bereich für immer auf dem Gerät, den das Konto verloren hat, während das Gerät gerade kein Token schickte.
- Der Server darf für einen Bereich jederzeit den vollständigen Stand schicken. Das Gerät ersetzt dann den lokalen Stand des Bereichs und wendet seine wartenden Kommandos erneut an.
- Untergrenze: Das Aufräumen entfernt Löschmarkierungen, die älter als 45 Tage sind, und hebt dabei die Untergrenze ihres Bereichs an ([Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)). Ein Token darunter könnte entfernte Löschungen verpasst haben und bekommt den vollständigen Stand. Jede Revision, die `/v1/sync` bedient, beachtet die Untergrenze, so trifft ein Zurücknehmen des Aufräumens oder ein Rollback auf eine ältere Revision nie auf einen Leser ohne sie.

#### Mitgelieferter Stand

Zu jedem Kommando, dessen Ergebnis weder `applied` noch das reservierte `accepted` ist, liefert der Server den aktuellen Stand seines Ziels mit, unabhängig vom Token. Das gilt auch für jedes `alreadyProcessed`, gleich mit welchem Original, und für `failed`. Ziel ist bei `alreadyProcessed` das gespeicherte des ersten Kommandos mit dieser ID, sonst das des Kommandos:

1. Sieht das Konto den Bereich nach der Verarbeitung nicht: nie ein Datensatz; der Bereich steht in `removedScopes`, außer er ist nur deshalb nicht sichtbar, weil sein Wurzel-Datentyp nicht in `dataTypes` steht ([Bereiche und Mitglieder](#bereiche-und-mitglieder)), bei `failed` nur, wenn das Token ihn nennt ([Entfernte Bereiche](#entfernte-bereiche)).
2. War das Kommando das `delete` eines Wurzel-Datensatzes: der vollständige Stand des Bereichs. Das Gerät hat den Bereich schon lokal entfernt, der Wurzel-Datensatz allein brächte die übrigen Datensätze nicht zurück.
3. Hat der Bereich in dieser Antwort schon den vollständigen Stand: nichts weiter.
4. Steht der Datentyp nicht in `dataTypes`: nichts.
5. Gibt es den Datensatz in diesem Bereich: der Datensatz im Block des Bereichs.
6. Sonst: `{ dataType, id }` in `deletedRecords` des Blocks, auch wenn die ID in einem fremden Bereich liegt, der nie genannt wird.

Der Server sucht nur im genannten Bereich, eine fremde Bereichs-ID gelangt so nie an das Gerät. Ein Kommando, das der Server nicht speichert, liefert sein Ziel mit, wenn sich `scopeId` und `recordId` als UUID lesen lassen (gleich welcher Version) und der Server den Datentyp kennt. Ohne diese Regel bliebe ein vorläufiger Wert dauerhaft auf dem Gerät stehen, wenn genau die Antwort mit der Ablehnung verloren geht.

#### Entfernte Bereiche

`removedScopes` nennt jeden Bereich, den das Token nennt oder auf den ein Kommando der Anfrage mit lesbarem Ziel und einem Ergebnis außer `failed` und dem reservierten `accepted` zielte (bei `alreadyProcessed` das gespeicherte), und den das Konto nach der Verarbeitung nicht sieht, außer er ist nur deshalb nicht sichtbar, weil sein Wurzel-Datentyp nicht in `dataTypes` steht. So verschwindet auch eine Liste, die ein anderes Gerät desselben Kontos gelöscht hat, bevor dieses Gerät sie je im Token hatte. Ein `failed` bringt dagegen nie einen Bereich in `removedScopes`, den nicht schon das Token hineinbringt: Das Gerät behält das Kommando und sendet es erneut; scheitert etwa das `create` einer neuen Liste, verlöre es sonst die Liste samt diesem Kommando. `removedByOwnAccount` ist `true`, wenn ein Gerät des Kontos oder der Server in dessen Namen ([Server als Schreiber](#server-als-schreiber)) den Wurzel-Datensatz des Bereichs gelöscht hat; Verlassen kommt mit [#129](https://github.com/Murmli/MurmliV2/issues/129) dazu. Das Gerät löscht den Bereich mit Datensätzen, Mitgliedern und wartenden Kommandos und legt einen Hinweis ab, außer bei `removedByOwnAccount: true`.

### Kompression

Der Server komprimiert eine Antwort genau dann mit gzip, wenn mindestens ein Block `fullState: true` hat und `Accept-Encoding` `gzip` mit einem q über 0 nennt. Dann trägt sie genau `Content-Encoding: gzip`, ein einziger Wert in Kleinbuchstaben, denn dart:io entpackt nur bei genau diesem Wert. Jede Antwort mit Status 200 trägt `Vary: Accept-Encoding`. Antworten ohne vollständigen Stand bleiben unkomprimiert ([Entscheidungen](#entscheidungen)), ebenso jede Antwort mit einem anderen Status als 200.

`Accept-Encoding` liest der Server nach RFC 9110 als Liste von Einträgen, getrennt durch Kommas:

- Groß- und Kleinschreibung zählen nicht, Leerzeichen um `;` sind erlaubt, ein Eintrag ohne `q` hat q = 1.
- Es zählt nur der erste Eintrag `gzip`, die Gewichte anderer Einträge, auch von `identity`, zählen nicht. `*`, `x-gzip` und `identity` nennen gzip nicht.
- Lässt sich der Header nicht vollständig lesen, etwa wegen eines q außerhalb von 0 bis 1 (`gzip;q=2`) oder eines anderen Parameters als `q` (`gzip;level=9`), oder enthält er keinen Eintrag, bleibt die Antwort unkomprimiert.
- Schließt der Header eine unkomprimierte Antwort aus (`identity;q=0`, `*;q=0`), ohne gzip zu erlauben, antwortet der Server trotzdem unkomprimiert, nie mit 406.

Wie stark der Server komprimiert, stellt `Sync:FullStateCompressionLevel` ein: `Optimal`, `Fastest` (Standard), `NoCompression` oder `SmallestSize`. Mit einem anderen Wert startet die API nicht.

### Fehler der ganzen Anfrage

Ein einzelnes Kommando lässt nie die ganze Anfrage scheitern. In den Fällen der Tabelle antwortet der Server mit Problem Details nach RFC 9457, auch ohne `Accept`-Header, und `type` ist eine Tag-URI `tag:murmli.de,2026:<code>`; ausgenommen sind die 415 aus dem Routing und die 401 aus der Anmeldung, beide ohne `type`. Die App macht jede zerstörende oder besondere Reaktion nur an `type` fest, nie am Statuscode allein, denn Zwischenstationen und das Framework liefern 401, 403, 404, 413, 415 und 503 auch selbst. Auf eine 401 erneuert sie wie bei jedem Endpunkt das Token der Anmeldung.

| Fall | Status | `<code>` | Header, Felder | Gerät |
| --- | --- | --- | --- | --- |
| Konto gibt es nicht (mehr), es wird gerade gelöscht oder wurde während der Anfrage gelöscht | 410 | `account-gone` | – | alle lokalen Daten samt Warteschlange, Token und Geräte-ID löschen, Anmeldung zeigen |
| App-Version wird nicht mehr unterstützt (reserviert, [#69](https://github.com/Murmli/MurmliV2/issues/69)) | 403 | `app-version-unsupported` | – | App bis zum Update sperren |
| Body über `maxRequestBytes` oder mehr als `maxCommandsPerRequest` Kommandos ([Vorgaben](#vorgaben)), nichts verarbeitet | 413 | `request-too-large` | Felder `maxRequestBytes` und `maxCommandsPerRequest`; eventuell `Connection: close` | mit halb so vielen Kommandos wiederholen; ist schon ein einzelnes Kommando zu groß, es aussortieren ([#124](https://github.com/Murmli/MurmliV2/issues/124)) |
| Rate-Limit: Anfragen je Gerät oder Konto, Kommandos je Konto | 429 | `rate-limited` | `Retry-After` in ganzen Sekunden, aufgerundet, mindestens 1 | genau so lange warten, dann wiederholen |
| Störung der Datenbank, Überlast, Frist der Anfrage (45 s) überschritten | 503 | `service-unavailable` | `Retry-After`, eine zufällige ganze Zahl von Sekunden zwischen `Database:UnavailableRetryAfter` (10 s) und dem Doppelten | genau so lange warten, dann wiederholen |
| `Murmli-Device-Id` fehlt oder ist keine UUID v4, Body kein JSON, Rahmen mit falschen Typen oder ohne `dataTypes` bzw. `commands` | 400 | `invalid-request` | – | später wiederholen, mit wachsendem Abstand |
| `Content-Type` fehlt | 415 | `invalid-request` | – | später wiederholen, mit wachsendem Abstand |
| `Content-Type` ist kein JSON, weder `application/json` noch ein `+json`-Typ | 415 aus dem Routing, ohne Body | – | – | später wiederholen, mit wachsendem Abstand |
| Anmeldung abgelaufen oder ungültig | 401 aus der Anmeldung, ohne `type` | – | `WWW-Authenticate` | Token der Anmeldung erneuern und wiederholen; lehnt `auth` das ab, weil die Sitzung beendet ist, greift die Abmeldung ([#54](https://github.com/Murmli/MurmliV2/issues/54)); ist `auth` nicht erreichbar, wie die letzte Zeile |
| alles andere: sonstige Statuscodes, Zeitüberschreitung, kein Netz, unlesbare oder abgeschnittene Antwort | – | – | – | später wiederholen, mit wachsendem Abstand, nichts verwerfen |

Ist `token` oder ein Eintrag in `dataTypes` kein gültiger Unicode-Text (etwa ein einzelnes Surrogate als Escape `\ud800`) oder enthält der Name eines Felds im Rahmen ein einzelnes Surrogate als Escape, zählt das als Rahmen mit falschem Typ und ergibt 400 `invalid-request`; den Wert eines unbekannten Felds liest der Server nicht. Unlesbar im Sinn von [Token](#token) ist nur ein lesbarer String mit ungültigem Inhalt.

Nach Anmeldung und Abruf des Kontos prüft der Server in dieser Reihenfolge: Konto (410), fehlender `Content-Type` (415), Geräte-ID (400), Anfragen je Gerät und Konto (429), Größe des Bodys (413), JSON und Rahmen (400), Zahl der Kommandos (413), Kommandos je Konto (429). Die ersten vier Prüfungen laufen, bevor er den Body liest. Einen vorhandenen `Content-Type`, der kein JSON ist, beantwortet schon das Routing mit 415, vor allen diesen Prüfungen.

Ein Gerät entfernt ein Kommando nur wegen eines Ergebnisses in einer lesbaren Antwort oder weil es das Kommando nach den Regeln aus [#124](https://github.com/Murmli/MurmliV2/issues/124) aussortiert, nie wegen eines Statuscodes: Schon eine Zwischenstation, die mit 400 antwortet, kostete sonst Änderungen.

```json
{ "type": "tag:murmli.de,2026:service-unavailable", "title": "Service Unavailable", "status": 503, "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" }
```

Den Typ `service-unavailable` mit `Retry-After` liefert schon heute jeder Endpunkt, wenn der Abruf des Kontos zu Beginn jeder Anfrage oder das Schreiben des Tags der letzten Anfrage an einer Störung der Datenbank scheitert ([api.md](api.md#problem-typen)).

### Vorgaben

Jede Antwort trägt in `settings` Vorgaben des Servers, damit sie sich ohne neue App-Version ändern lassen, wenn die Zahl der Geräte wächst ([#140](https://github.com/Murmli/MurmliV2/issues/140)).

| Vorgabe | Standard | Bedeutung |
| --- | --- | --- |
| `pollIntervalSeconds` | 60 | Abstand des regelmäßigen Abrufs in Sekunden ([#126](https://github.com/Murmli/MurmliV2/issues/126)) |
| `maxCommandsPerRequest` | 500 | Höchstzahl der Kommandos je Anfrage, das Gerät darf weniger senden |
| `maxRequestBytes` | 1000000 | Höchstgröße des UTF-8-Bodys einer Anfrage in Byte |

- Jede Vorgabe ist für die App optional: Fehlt eine, nimmt das Gerät seinen eingebauten Wert, für `maxRequestBytes` 1.000.000. Innerhalb von `v1` können weitere dazukommen, etwa zur Live-Verbindung ([#125](https://github.com/Murmli/MurmliV2/issues/125)).
- `maxRequestBytes` bindet das Gerät: Es serialisiert jede Anfrage und schickt weniger Kommandos, wenn der Body sonst größer wäre. Ein einzelnes Kommando, das allein die Grenze überschreitet, sortiert es aus wie nach „Anfrage zu groß“ ([#124](https://github.com/Murmli/MurmliV2/issues/124)). Die 413 des Servers ist nur ein Sicherheitsnetz.
- Am Server kommen die Werte aus `Sync:PollIntervalSeconds`, `Sync:MaxCommandsPerRequest` und `Sync:MaxRequestBytes`.

### Verträglichkeit

- Bis zum ersten Live-Gang darf sich der Vertrag in `v1` auch inkompatibel ändern. Danach ändert er sich innerhalb einer Version nur rückwärtskompatibel: neue Felder, Datentypen, Arten von Bereichen, Kommandoarten, Gründe, Ursachen, Rollen und Vorgaben. Alles andere bekommt dann eine neue Version des Endpunkts ([api.md](api.md#versionierung)). Es laufen immer die aktuelle Version der API und ihre Vorgängerin.
- Der Server ignoriert unbekannte Felder im Rahmen der Anfrage, damit ein Rollback der API neuere Apps nicht abweist. Ein unbekanntes Feld eines Kommandos oder in `fields` macht dieses Kommando `invalid`. Das Backend wird deshalb immer vor der App-Version ausgeliefert, die ein neues Feld schickt.
- Das Gerät ignoriert Felder und Datentypen, die es nicht kennt; der Server schickt ihm ohnehin nur die Datentypen aus `dataTypes`. Ein Datentyp muss mit Werten umgehen können, die seine Version der App nicht kennt, etwa einem neuen Schlüssel einer Aufzählung, und ein einzelner unlesbarer Datensatz macht nie die ganze Antwort unbrauchbar.
- Lernt die App mit einem Update neue Datentypen oder Felder, verwirft sie ihr Token und holt einmal den vollständigen Stand.

Werte sind Strings in camelCase, gleich in Vertrag, Datenbank und Doku:

| Feld | Werte | Reserviert, nie erzeugt | Unbekannter Wert |
| --- | --- | --- | --- |
| `kind` eines Kommandos | `create`, `setFields`, `delete` | Kommandoarten einzelner Datentypen | am Server `invalid` |
| `status` | `applied`, `ignored`, `rejected`, `alreadyProcessed`, `failed` | `accepted` | geschlossene Menge, kommt nicht vor |
| `reason` bei `rejected` | `noAccess`, `limitExceeded`, `invalid`, `clockAhead`, `tooOld` | `accountMayNotWrite` | am Gerät wie `invalid` |
| `cause` bei `ignored` | `lostToNewer`, `targetDeleted` | – | am Gerät wie `lostToNewer` |
| `role` eines Mitglieds | `owner` | `member`, `readOnly` | am Gerät hinnehmen |
| `state` eines Mitglieds | `active` | `former` | geschlossene Menge, kommt nicht vor |
| `kind` eines Bereichs | `personal`, `shoppingList`, in Tests `testBoard` | weitere geteilte Arten | kommt nicht vor: der Server schickt nur Bereiche, deren Wurzel-Datentyp in `dataTypes` steht |

Geschlossene Mengen kennt das Gerät samt ihrer reservierten Werte von Anfang an.

### Reserviert

- `status` `accepted`: Der Server hat das Kommando entgegengenommen, aber noch nicht verarbeitet, für eine spätere Warteschlange im Backend ([#140](https://github.com/Murmli/MurmliV2/issues/140)). Das Gerät behält es, zählt es nicht als Fehlversuch und sendet es später erneut, nach dem nächsten Anstoß, sonst mit dem regelmäßigen Abruf. Ein Kommando, das nur `accepted` ist, löst keine Runden ohne Pause aus.
- `hasMore`: „es gibt mehr“, für Antworten in Seiten. Bei `true` startet das Gerät sofort die nächste Runde.
- `reason` `accountMayNotWrite`: Konto darf nicht schreiben ([#133](https://github.com/Murmli/MurmliV2/issues/133)).
- `role` `member` (mit dem Teilen, [#143](https://github.com/Murmli/MurmliV2/issues/143)) und `readOnly`, `state` `former` (ehemaliges Mitglied).
- Problemtyp `app-version-unsupported` mit 403: Die App sperrt sich bis zum Update. Wie die App ihre Version nennt und welche Versionen der Server ablehnt, entscheidet [#69](https://github.com/Murmli/MurmliV2/issues/69); bis dahin gibt es keinen Header dafür.
- Das Format-Byte des Tokens, für ein späteres anderes Format.

## Konfliktregeln

- **Je Feld gewinnt die spätere Zeit.** Der Server merkt sich je Feldgruppe den Zeitpunkt der letzten übernommenen Änderung und übernimmt eine Gruppe, wenn `time` mindestens so groß ist. Bei gleicher Zeit gewinnt also das später verarbeitete Kommando, so zählt bei zwei schnellen Aktionen desselben Geräts die zweite. Gruppen gewinnen einzeln: Ändert ein Gerät die Menge und ein anderes die Kategorie, bleiben beide. Ist mindestens eine Gruppe übernommen, ist das Ergebnis `applied` und diese Gruppen tragen danach `time`; sonst `ignored` mit `lostToNewer`.
- **Löschen gewinnt immer**, ohne Blick auf die Zeit. Ein gelöschter Datensatz kommt nicht wieder, auch nicht mit derselben ID: Jedes spätere Kommando an ihn in seinem Bereich ist `ignored` mit `targetDeleted`, nach dem Aufräumen seiner Löschmarkierung weiter jedes `setFields` und `delete`; gegen ein erneutes Anlegen schützt dann, dass nur das ursprüngliche `create` die ID trägt und dieses `alreadyProcessed` oder `tooOld` ist ([Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)).
- **`create` auf eine vorhandene ID** im selben Bereich wirkt wie `setFields` mit allen gesendeten Gruppen; Felder, die nur beim Anlegen gelten, und die Angaben des Servers zum Anlegen (etwa `createdAt`) bleiben. Liegt die ID lebend oder mit Löschmarkierung in einem anderen Bereich: `noAccess`; ist die Löschmarkierung dort aufgeräumt, gilt die ID als unbekannt (Schritt 11, [Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)).
- **`setFields` und `delete` ohne Ziel:** Gibt es den Datensatz nicht oder nicht mehr, ist das Kommando `ignored` mit `targetDeleted`; es legt nie einen Datensatz an. Darauf beruht, dass alte Löschmarkierungen aufgeräumt werden dürfen.
- **Regeln eines Datentyps** können ein Kommando zusätzlich ignorieren (`lostToNewer`) oder ablehnen (vorhandener Grund).
- **Zeitpunkt:** Liegt `time` mehr als 5 Minuten nach der Uhr des Servers, lehnt er das Kommando als `clockAhead` ab; es verfällt mit Hinweis, statt zu warten. Liegt `time` mehr als 45 Tage davor, lehnt er es als `tooOld` ab: Wiederholungen erkennt er nur an gespeicherten Kommando-IDs, und eine ID räumt er erst auf, wenn ein erneut gesendetes übernommenes Kommando ohnehin `tooOld` wäre; ohne die Ablehnung legte ein spät erneut gesendetes `create` einen längst gelöschten Datensatz wieder an. Ist die ID noch gespeichert, gewinnt `alreadyProcessed`.
- **Der Server als Schreiber** nutzt dieselben Regeln mit seiner eigenen Uhr als `time`: Wiederholbarkeit, Werte und Grenzen, Regeln des Datentyps, Konfliktregeln, Löschen gewinnt, keine Wiederkehr, Versionen und Löschmarkierungen. Er schreibt im Namen eines Kontos, etwa beim Anlegen der ersten Liste, oder ohne Person, etwa beim Entfernen abgehakter Artikel, und darf Felder des Servers ohne Füllung setzen, außer Verweisen auf Personen. Prüfungen der Uhr, der Mitgliedschaft und der Rolle, Rate-Limits und später die Prüfung „Konto darf nicht schreiben ([#133](https://github.com/Murmli/MurmliV2/issues/133))“ entfallen, und eine Bedingung kann eine Änderung verwerfen (Schritt 14). Geräte erfahren seine Änderungen wie jede andere. Aufruf und Ergebnisse: [Server als Schreiber](#server-als-schreiber).

Reihenfolge der Entscheidung, nach den Prüfungen vor der Verarbeitung ([Ergebnisse](#ergebnisse)):

| Schritt | Lage | Ergebnis |
| --- | --- | --- |
| 1 | Kommando-ID schon verarbeitet | `alreadyProcessed` mit dem gespeicherten Ergebnis |
| 2 | `create` eines Wurzel-Datensatzes, Konto wird oder wurde gelöscht | die ganze Anfrage 410 |
| 3 | `create` eines Wurzel-Datensatzes, Bereich vorhanden | weiter als `setFields` auf den Wurzel-Datensatz, wenn das Konto den Bereich sieht, sonst `noAccess` |
| 4 | `create` eines Wurzel-Datensatzes, Bereich fehlt | an der Grenze eigener Bereiche `limitExceeded`, sonst neuer Bereich mit dem Absender als Besitzer |
| 5 | anderes Kommando, Bereich fehlt | `noAccess`, nichts entsteht |
| 6 | kein Zugriff: persönlicher Bereich eines anderen Kontos oder kein aktiver Mitgliedseintrag | `noAccess` |
| 7 | Datentyp gehört zu einer anderen Art von Bereich | `invalid`, erst nach dem Zugriff, damit ein fremder Bereich nichts verrät |
| 8 | Rolle reicht nicht; den Wurzel-Datensatz löscht nur der Besitzer | `noAccess` |
| 9 | Datensatz oder Löschmarkierung liegt in einem anderen Bereich | `noAccess`, dieselbe Antwort wie ohne Recht |
| 10 | Datensatz fehlt, Löschmarkierung in diesem Bereich | `ignored` mit `targetDeleted` |
| 11 | Datensatz fehlt, keine Löschmarkierung | `create`: neuer Datensatz; `setFields`, `delete`: `ignored` mit `targetDeleted` |
| 12 | neuer Datensatz an der Grenze je Bereich | `limitExceeded` |
| 13 | Konfliktregeln | übernommene Gruppen oder `ignored` mit `lostToNewer` |
| 14 | Bedingung einer Änderung des Servers nicht erfüllt, geprüft vor Schritt 13 am Datensatz ohne die Werte des Kommandos | `ignored` mit `lostToNewer` |
| 15 | Regeln des Datentyps | `ignored` oder `rejected` |
| 16 | Felder des Servers | gesetzt, etwa `changedBy` und `changedAt` |

## Einkaufsliste

Erster Datentyp ([#141](https://github.com/Murmli/MurmliV2/issues/141)). Jede Liste ist ein geteilter Bereich der Art `shoppingList`, ihr Wurzel-Datensatz ist die Liste selbst (`shoppingList`, ID gleich Bereichs-ID), ihre Artikel sind Datensätze vom Typ `shoppingListItem` im selben Bereich. In diesem Kapitel verweisen Zahlen in Klammern wie in den Issues auf Fragen im internen Fragenkatalog.

Im Code: die Entitäten `ShoppingList` und `ShoppingListItem` in `Murmli.Backend.Data/ShoppingLists` (Schema `shopping`, Tabellen `lists` und `items`), die Datentypen `ShoppingListType` und `ShoppingListItemType`, der Katalog `ShoppingCatalog`, die Beiträge `FirstShoppingListContributor` und `CheckedItemsCleanupContributor` und die Optionen `ShoppingListOptions` in `Murmli.Backend.Api/ShoppingLists`.

`shoppingList`:

| Feld | Rolle | Wert |
| --- | --- | --- |
| `name` | eigene Gruppe, Pflicht bei `create` | Text, 0 bis 100 UTF-16-Einheiten, leer erlaubt |

`shoppingListItem`:

| Feld | Rolle | Wert |
| --- | --- | --- |
| `name` | eigene Gruppe, Pflicht bei `create` | Text, 1 bis 256 UTF-16-Einheiten |
| `quantity` | Gruppe `amount` mit `unit` | Zahl über 0 bis `999999.999` mit höchstens 3 Nachkommastellen, oder `null` |
| `unit` | Gruppe `amount` mit `quantity` | Einheit aus dem Katalog oder `null`, unabhängig von `quantity` |
| `category` | eigene Gruppe, Pflicht bei `create` | Kategorie aus dem Katalog |
| `checked` | eigene Gruppe | `true` oder `false`, ohne Wert bei `create` `false` |
| `language` | nur bei `create`, Pflicht | 2 bis 3 Kleinbuchstaben von `a` bis `z`, sonst nichts (am Server `\A[a-z]{2,3}\z`; `^[a-z]{2,3}$` ließe in .NET einen Zeilenumbruch am Ende zu) |
| `createdAt` | Feld des Servers | `time` des `create`, beim Server als Schreiber also seine Uhr |
| `changedAt` | Feld des Servers | `time` der letzten übernommenen Änderung, auch beim Anlegen, Abhaken und Herausnehmen des Hakens |
| `changedBy` | Feld des Servers | Mitglieds-ID der Person, die zuletzt geändert hat („zuletzt geändert von“), auch beim Anlegen, Abhaken und Herausnehmen des Hakens; `null`, wenn der Server den Artikel ohne Person angelegt hat |

- Als Angabe zur Person zeigen Geräte an einem Artikel nur „zuletzt geändert von“ (`changedBy`). Wer ihn angelegt hat, hält der Server zusätzlich intern fest, für eine Datenauskunft und das Entfernen der Daten einer Person ([#131](https://github.com/Murmli/MurmliV2/issues/131)), und schickt es nie an Geräte.
- `changedAt` und `changedBy` beschreiben immer dieselbe Änderung: Ändert der Server einen Artikel ohne Person, bleiben beide stehen.
- Abgehakte Artikel bleiben änderbar, die Einkaufsliste hat keine eigenen Regeln, die Kommandos ignorieren oder ablehnen.
- Kategorien (22) und Einheiten (11) sind feste Schlüssel in camelCase, festgelegt in `src/sync-cases/shopping/catalog.json` (`{ "categories": [...], "units": [...] }`, alphabetisch; die Reihenfolge hat keine Bedeutung). Der Server führt dieselben in `ShoppingCatalog`, die App übernimmt sie für die Kategorien und Einheiten aus [#80](https://github.com/Murmli/MurmliV2/issues/80). Bis zum ersten Live-Gang dürfen Schlüssel sich ändern ([Verträglichkeit](#verträglichkeit)), Daten mit alten Schlüsseln in Dev setzt dann ein Mensch zurück: Er löscht die Datenbank `murmli` und legt sie neu an oder löscht die betroffenen Konten. Die Datenbank löscht und erstellt er dabei nur über Azure (`az postgres flexible-server db delete` und `db create`), nie per `DROP DATABASE` und `CREATE DATABASE` mit der eigenen Rolle: Nur so gehört sie wieder `azure_pg_admin`, sonst darf `backend-identity` keine Schemas anlegen ([database.md](database.md#schemas)). Danach startet er `api` und `auth` neu (`az containerapp revision restart`), denn beide migrieren nur beim Start; jedes Konto beginnt neu, Produkte kommen mit dem nächsten Lauf von `product-import` zurück. Zeilen abgeglichener Tabellen ändert oder löscht er dafür nie direkt: Das umginge die Versionen ([Tragende Regel](#tragende-regel)), und Geräte behielten den alten Stand. Nach dem ersten Live-Gang kommen Schlüssel nur hinzu, nie werden sie umbenannt oder entfernt; eine neue Kategorie ist ein zusätzlicher Schlüssel in beiden.
- Erste Liste: Mit dem persönlichen Bereich legt der Server beim ersten Abgleich eines Kontos genau eine Liste an, auch wenn sich zwei Geräte gleichzeitig zum ersten Mal anmelden. Ihr Name ist leer, die App zeigt dafür einen übersetzten Standardnamen. Wer sie löscht, hat danach keine Liste, der Server legt keine neue an. Angelegt wird sie von `FirstShoppingListContributor` im Namen des Kontos ([Erster Abgleich eines Kontos](#erster-abgleich-eines-kontos)). Konten, deren persönlicher Bereich vor der Einkaufsliste am Server entstand, haben keine erste Liste; vor dem ersten Live-Gang hingenommen, ein solches Konto bekommt sie nach dem Löschen und erneuten Anmelden.
- Abgehakte Artikel entfernt der Server beim Aufräumen, sobald das Abhaken mehr als 30 Tage zurückliegt (`ShoppingLists:CheckedItemRetention`); maßgeblich ist die Ankunft des Abhakens am Server, nicht die Uhr des Geräts. Nimmt jemand den Haken vorher heraus, bleibt der Artikel. Geräte erfahren das wie jede Löschung. Die Ankunft hält die interne Spalte `checked_arrived_at`, nie an Geräte: Die Regel des Datentyps setzt sie auf die Uhr des Servers, wenn eine übernommene Gruppe `checked` den Artikel abhakt, und leert sie beim Herausnehmen des Hakens. `CheckedItemsCleanupContributor` löscht jeden Artikel, dessen Abhaken länger als `ShoppingLists:CheckedItemRetention` (`00:01:00` bis `3650.00:00:00`, sonst startet die API nicht) zurückliegt, als Server ohne Person (`SyncActor.System`) mit der Bedingung, dass er noch abgehakt ist und `checked_arrived_at` unverändert, geprüft unter der Sperre der Liste. So gewinnt ein Herausnehmen des Hakens, das während des Laufs ankommt.
- Eine Liste hat keine Notiz (33).
- Die Reihenfolge der Kategorien gilt je Person (123a) und ist kein Feld der Liste. Abgeglichen wird sie erst später, mit den Einstellungen zur Ansicht als Datentyp im persönlichen Bereich. Bis dahin bleibt sie auf dem Gerät ([#86](https://github.com/Murmli/MurmliV2/issues/86)) und wird mit allen lokalen Daten gelöscht: beim Abmelden, bei einer abgelaufenen Sitzung und bei „Konto existiert nicht mehr“ ([#124](https://github.com/Murmli/MurmliV2/issues/124)).
- Artikel tragen kein Feld für eine Markierung durch die KI. Der Bezug zu Rezepten kommt erst mit dem Übertragen von Rezeptzutaten auf die Liste (120).

| Aktion | Kommando | Regel |
| --- | --- | --- |
| Artikel hinzufügen | `create` | Fügen zwei Geräte dasselbe hinzu, entstehen zwei Artikel; ein abgehakter Artikel mit gleichem Namen bleibt; über 1.000 Artikel je Liste `limitExceeded`. |
| Name, Menge und Einheit, Kategorie ändern | `setFields` | je Gruppe gewinnt die spätere Zeit |
| Abhaken, Haken herausnehmen, rückgängig machen | `setFields` mit `checked` | die spätere Zeit gewinnt; eine Mengenänderung berührt `checked` nie |
| Artikel löschen | `delete` | Löschen gewinnt, spätere Änderungen sind `ignored` mit `targetDeleted` |
| Abgehakte entfernen, Liste leeren | je Artikel, den das Gerät kennt, ein `delete` | Artikel, die das Gerät nicht kannte, bleiben; ein Sammelkommando gibt es nicht |
| Artikel ersetzen ([#90](https://github.com/Murmli/MurmliV2/issues/90)) | `delete` und `create` | Sache der App, der Server bleibt neutral |
| Liste anlegen | `create` der Liste | geht ohne Netz; über 42 eigene Listen `limitExceeded`, die Antwort meldet den Bereich als entfernt, das Gerät entfernt ihn mit Hinweis |
| Liste umbenennen | `setFields` mit `name` | die spätere Zeit gewinnt |
| Liste löschen | `delete` der Liste | nur der Besitzer; für alle weg |

Entschieden, noch nicht gebaut: die Einkaufsliste in der App. Entfällt, sobald sie mit [#142](https://github.com/Murmli/MurmliV2/issues/142) an den Abgleich angebunden ist.

## Grenzen

| Grenze | Wert | Antwort |
| --- | --- | --- |
| Größe des Bodys | 1.000.000 Byte (UTF-8) | 413, nichts verarbeitet |
| Kommandos je Anfrage | 500 | 413, nichts verarbeitet |
| Feldwerte und Längen | je Datentyp, etwa Name eines Artikels 1 bis 256 UTF-16-Einheiten | nur dieses Kommando `rejected` mit `invalid`, die übrigen laufen weiter |
| Datensätze je Bereich | je Datentyp, Artikel je Liste 1.000 | `rejected` mit `limitExceeded` |
| Eigene Bereiche je Konto | je Wurzel-Datentyp, eigene Listen 42 | `rejected` mit `limitExceeded`, auch bei gleichzeitigen Anfragen genau |
| Anfragen je Gerät | 120 je Minute | 429 |
| Anfragen je Konto | 600 je Minute | 429 |
| Kommandos je Konto | bis zu 2.000 auf einmal, danach 33 je Minute nachgefüllt (im Mittel rund 2.000 je Stunde); nach einer Pause höchstens rund 4.000 innerhalb einer Stunde | 429 |
| Löschungen in einem Delta | Schwelle: Summe der Höchstzahlen an Datensätzen je Bereich über die Datentypen der Art, bei Listen 1.001 | vollständiger Stand statt Löschungen |
| Zeit für Kommandos | 20 s je Anfrage, das erste Kommando läuft immer | Ergebnisse nur für die vorderen Kommandos |

- Die Werte sind Standards im Code, Umgebungen können sie überschreiben (`Sync:MaxRequestBytes`, `Sync:MaxCommandsPerRequest`, `Sync:CommandPhaseBudget`, `Sync:RateLimits:DeviceRequestsPerMinute`, `Sync:RateLimits:AccountRequestsPerMinute`, `Sync:RateLimits:AccountCommandsPerHour`, `Sync:DataTypes:<name>:MaxRecordsPerScope`, `Sync:DataTypes:<name>:MaxOwnedScopesPerAccount`); Längen von Feldern stehen nur im Code. Die App prüft dieselben Grenzen der Datentypen vor dem Einreihen, so entsteht kein Kommando, das der Server nie annimmt ([#124](https://github.com/Murmli/MurmliV2/issues/124)).
- Die Grenze je Gerät gilt je Konto und Gerät, eine geratene Geräte-ID bremst kein anderes Konto. Erneut gesendete, schon verarbeitete Kommandos zählen mit. Die Zähler gelten je Instanz der API.
- Anfragen je Gerät und je Konto zählen in festen Fenstern von einer Minute; `Retry-After` nennt die Zeit bis zum Ende des laufenden Fensters. Kommandos je Konto fasst ein Eimer mit `Sync:RateLimits:AccountCommandsPerHour` Plätzen, den der Server laufend um ein Sechzigstel davon je Minute auffüllt, abgerundet und mindestens um eins; `Retry-After` nennt die ganzen Minuten, bis genug nachgefüllt ist.
- Ein Gerät, das genau `Retry-After` wartet, kann noch einmal 429 bekommen, bei jeder der drei Grenzen: Bei Anfragen je Gerät und je Konto wechselt das Fenster nur in Schritten von 100 ms, oder statt der einen der beiden Grenzen greift nun die andere; bei Kommandos je Konto kann `Retry-After` bis zu einer Minute zu kurz sein, die nächste 429 nennt dann eine Minute. Das Gerät behandelt jede weitere 429 wie die erste und verliert nichts.
- Was gezählt ist, gibt der Server nicht zurück, und die Grenze, die eine Anfrage abweist, zählt sie nicht. Hat das Konto seine Anfragen im laufenden Fenster schon verbraucht, weist der Server die Anfrage ab, bevor sie bei einer der beiden Grenzen zählt. Sonst zählt sie bei Anfragen je Gerät, sobald sie diese Grenze passiert, auch wenn sie danach an der Grenze je Konto scheitert; bei Anfragen je Konto, sobald sie auch diese passiert, auch wenn sie danach mit 413, 400, 429, 410 oder 503 endet. Eine an der Grenze je Gerät abgewiesene Anfrage zählt beim Konto nicht, so verbraucht ein Gerät in einer Schleife nicht die Anfragen der übrigen Geräte des Kontos. Kommandos je Konto zählt der Server erst nach der Prüfung ihrer Zahl und nur bei Anfragen mit Kommandos: Eine dort abgewiesene Anfrage verbraucht keine Kommandos, und eine Anfrage ohne Kommandos geht auch dann durch, wenn keine Kommandos mehr frei sind.
- `Sync:RateLimits:AccountCommandsPerHour` muss mindestens `Sync:MaxCommandsPerRequest` sein, sonst startet die API nicht: Eine Anfrage mit der größten erlaubten Zahl an Kommandos käme sonst nie durch.

## Aufbewahrung und Aufräumen

Löschmarkierungen und Kommando-IDs bleiben 45 Tage (`Sync:Retention`); dieselbe Dauer gilt für die Ablehnung zu alter Kommandos als `tooOld` ([Zeit](#zeit)). Danach entfernt sie das Aufräumen ([#132](https://github.com/Murmli/MurmliV2/issues/132)). Die Dauer ist nur eine Kostenfrage: Ein Gerät, dessen Token unter der Untergrenze eines Bereichs liegt, lädt den Bereich einmal vollständig und verliert nichts ([Vollständiger Stand und Änderungen](#vollständiger-stand-und-änderungen)).

Den Lauf startet der Hintergrunddienst `SyncCleanupService` der API eine Minute nach ihrem Start (`Sync:Cleanup:InitialDelay`) und danach alle 24 Stunden (`Sync:Cleanup:Interval`), solange die Instanz läuft; `Sync:Cleanup:Enabled=false` schaltet ihn ab. Die Richtigkeit hängt nicht am Zeitplan, ein ausgefallener Lauf verschiebt nur Kosten. Betrieb in Azure: [azure.md](azure.md#aufräumen-des-abgleichs). Ein Lauf (`SyncCleanupRunner.RunAsync`) geht so vor:

1. Lease: Eine Anweisung trägt den Lauf in die einzige Zeile von `sync.cleanup_lease` ein, wenn sie frei oder abgelaufen ist, für `Sync:Cleanup:LeaseDuration` (10 Minuten). Sonst endet der Lauf ohne Arbeit mit `SyncCleanupSkipped`. So läuft über alle Instanzen, Revisionen und Aufrufe auf Abruf höchstens ein Lauf. Der Lauf nimmt die Lease einmal und verlängert sie nie, bricht eine Minute vor ihrem Ende ab und gibt sie am Ende frei, auch nach einem Fehler oder Abbruch.
2. Löschmarkierungen, je Bereich in einer eigenen Transaktion: Die Untergrenze des Bereichs steigt auf die höchste Version unter seinen bis zu 1.000 (`Sync:Cleanup:BatchSize`) abgelaufenen Löschmarkierungen mit den kleinsten Versionen, danach löscht dieselbe Transaktion die abgelaufenen Löschmarkierungen bis zu dieser Version. Bricht der Lauf ab, gilt beides oder keins. Jüngere Löschmarkierungen bleiben, auch wenn ihre Version nach einem Sprung der Uhr unter der Untergrenze liegt. Hat ein Bereich mehr abgelaufene Löschmarkierungen, kommt er in der nächsten Runde wieder dran. Bekommt eine Transaktion die Sperre ihres Bereichs nicht (`Sync:LockTimeout`, auch nach den Wiederholungen), bleibt der Bereich bis zum nächsten Lauf liegen.
3. Kommando-IDs in Portionen zu 1.000, jede in einer eigenen Transaktion, bis eine Portion leer bleibt. Entfernt wird eine ID, wenn `LEAST(time, processed_at)` mehr als Aufbewahrung plus Sicherheitsabstand (`Sync:Cleanup:SafetyMargin`, 1 Tag, mindestens `Sync:ClockAheadTolerance` plus `Sync:RequestDeadline`) zurückliegt. Angewendete Löschungen eines Wurzel-Datensatzes bleiben bis zur Löschung des Kontos, aus ihnen kommt `removedByOwnAccount` ([Entfernte Bereiche](#entfernte-bereiche)).
4. Beiträge in der Reihenfolge ihrer Registrierung, in der Zeit, die die Schritte 2 und 3 übrig lassen: zuerst `StaleAccountDeletionContributor` der API, der hängengebliebene Kontolöschungen abschließt ([Kontolöschung](#kontolöschung)), danach die der Datentypen, derzeit `CheckedItemsCleanupContributor` der Einkaufsliste ([Beiträge der Datentypen](#beiträge-der-datentypen)). Wirft ein Beitrag eine Exception, die weder eine Störung der Datenbank noch ein Abbruch des Laufs (durch den Aufrufer oder die Zeitgrenze) ist, loggt der Lauf `SyncCleanupFailed` und macht mit dem nächsten Beitrag weiter.
5. `SyncCleanupCompleted` mit der Dauer und der Zahl entfernter Löschmarkierungen, Kommando-IDs und Datensätze (Summe der Beiträge).

Eine Störung der Datenbank, jede andere Exception eines eigenen Schritts und ein Abbruch durch den Aufrufer (Host, Test, Steuer-App) beenden den Lauf mit `SyncCleanupFailed`; als Exception gibt `RunAsync` nur den Abbruch durch den Aufrufer weiter. Erreicht er seine Zeitgrenze, endet er ohne Exception, aber mit `SyncCleanupStopped` (Warning, mit denselben Zahlen wie `SyncCleanupCompleted`) statt `SyncCleanupCompleted`; den Rest erledigt der nächste Lauf. Jeder Schritt ist wiederholbar, und die Untergrenze steigt nur. Zeitplan und Lease folgen dem `TimeProvider` (in Tests der gestellten Uhr), die Zeitgrenze eines Laufs der Echtzeit. Logs: [telemetry.md](telemetry.md#abgleich).

Warum nichts Gelöschtes wiederkommt:

- Ein Kommando, dessen `time` mehr als 45 Tage vor der Uhr des Servers liegt, lehnt der Server als `tooOld` ab ([Konfliktregeln](#konfliktregeln)).
- Eine Kommando-ID verschwindet erst, wenn `LEAST(time, processed_at)` älter ist als Aufbewahrung plus Sicherheitsabstand. Ein übernommenes Kommando hatte `time` höchstens 5 Minuten (`Sync:ClockAheadTolerance`) nach `processed_at`, sendet ein Gerät es nach dem Entfernen seiner ID erneut, ist es also `tooOld`. Der Sicherheitsabstand deckt zusätzlich die Frist einer Anfrage (`Sync:RequestDeadline`) ab: Der Server prüft `time` gegen seine Uhr unmittelbar vor der Verarbeitung des Kommandos und speichert die Kommando-ID bis zu dieser Frist später, womöglich erst nach einem Lauf, der die alte ID entfernt hat.
- `setFields` und `delete` legen nie etwas an, eine fehlende Löschmarkierung bringt deshalb keinen Datensatz zurück. Anlegen könnte ihn nur das ursprüngliche `create`, und das ist dann schon verarbeitet (`alreadyProcessed`, solange der Server seine Kommando-ID kennt) oder zu alt (`tooOld`). Ebenso entsteht ein Bereich nur mit dem ursprünglichen `create` seines Wurzel-Datensatzes.
- Abgelehnte und ignorierte Kommandos hatten keine Wirkung, ihre erneute Prüfung nach dem Entfernen der ID ist unschädlich.

| Einstellung | Standard | Erlaubt |
| --- | --- | --- |
| `Sync:Retention` | `45.00:00:00` | `00:01:00` bis `3650.00:00:00`; zugleich die Grenze für `tooOld` |
| `Sync:Cleanup:Enabled` | `true` | `false` schaltet den geplanten Lauf ab |
| `Sync:Cleanup:InitialDelay` | `00:01:00` | `00:00:00` bis `1.00:00:00` |
| `Sync:Cleanup:Interval` | `1.00:00:00` | `00:01:00` bis `7.00:00:00` |
| `Sync:Cleanup:BatchSize` | 1000 | 1 bis 100.000; Zeilen je Anweisung, Bereiche je Abfrage der Löschmarkierungen, abgehakte Artikel je Abfrage, Konten je Abfrage hängengebliebener Kontolöschungen |
| `Sync:Cleanup:SafetyMargin` | `1.00:00:00` | `00:00:00` bis `30.00:00:00`, mindestens `Sync:ClockAheadTolerance` plus `Sync:RequestDeadline` |
| `Sync:Cleanup:LeaseDuration` | `00:10:00` | `00:02:00` bis `1.00:00:00`; der Lauf endet eine Minute vorher |

Mit einem Wert außerhalb startet die API nicht. Verschachtelte Optionen wie `SyncOptions.Cleanup` prüft `ValidateDataAnnotations()` nur mit `[ValidateObjectMembers]` an der Eigenschaft; jede weitere verschachtelte Optionsklasse unter `Sync`, deren Grenzen als Attribute stehen, braucht es ebenso.

### Beiträge der Datentypen

Fachliches Aufräumen, etwa das Entfernen abgehakter Artikel, hängt ein Datentyp mit einer Klasse ein, die `ISyncCleanupContributor` (`Murmli.Backend.Sync.Cleanup`) umsetzt und mit `services.AddSyncCleanupContributor<T>()` registriert ist (scoped, je Lauf ein Scope). `RunAsync(SyncCleanupContext context, CancellationToken cancellationToken)` bekommt die Uhr des Laufs (`Now`, auf Millisekunden gekürzt) und die Portionsgröße (`BatchSize`) und liefert die Zahl der entfernten Datensätze. Entfernt ein Beitrag Datensätze eines Datentyps, löscht er sie über den Kern, als Server, der schreibt ([Konfliktregeln](#konfliktregeln)), mit Löschmarkierung und neuer Version wie bei einem Gerät; seine Löschmarkierungen altern danach normal. Er beachtet den Abbruch zwischen seinen Portionen, macht nach jedem Ergebnis eines Schreibens, auch `failed`, mit dem nächsten Datensatz weiter, überspringt einen Datensatz, dessen Bereich die Sperre nicht rechtzeitig hergibt, bis zum nächsten Lauf und hat mit der Lease nichts zu tun. Eine Sperre, die nicht rechtzeitig frei wird, erkennt ein Beitrag an `exception.IsLockTimeout()` (`Murmli.Backend.Data.Extensions`, 55P03 in jeder Tiefe der inneren Exceptions): `SyncServerWriter.WriteAsync` liefert dafür kein Ergebnis, sondern wirft die Störung weiter ([Server als Schreiber](#server-als-schreiber)). Derzeit hängt die Einkaufsliste `CheckedItemsCleanupContributor` ein ([Einkaufsliste](#einkaufsliste)). Er liest die abgelaufenen Artikel in Portionen zu `Sync:Cleanup:BatchSize`, bis keine mehr kommen, und schließt bei einer Sperre, die nicht rechtzeitig frei wird, die ganze Liste und sonst jeden Artikel, den er nicht entfernt (Ergebnis außer `applied`), für den Rest des Laufs aus; der nächste Lauf versucht sie wieder. Ein sehr großer Rückstand abgehakter Artikel kann so den Rest der Zeitgrenze aufbrauchen, den Löschmarkierungen und Kommando-IDs übrig lassen; der Lauf endet dann mit `SyncCleanupStopped`, und spätere Läufe holen den Rest nach. Registriert ist er nach `StaleAccountDeletionContributor` ([Kontolöschung](#kontolöschung)), damit ein solcher Rückstand keine Kontolöschung aufhält. `StaleAccountDeletionContributor` löscht keine einzelnen Datensätze als Server, der schreibt, sondern ganze Bereiche eines Kontos samt ihren Datensätzen, zählt diese nicht (liefert 0) und überspringt ein Konto nie wegen einer Sperre: Gibt ein Bereich die Sperre nicht rechtzeitig her, endet der Lauf mit `SyncCleanupFailed`, und der nächste Lauf wiederholt die Löschung. Scheitert nur `auth`, bleibt das Konto bis zum nächsten Lauf markiert, und der Beitrag macht mit dem nächsten Konto weiter. Bewusst ohne Überspringen: Vor dem Teilen ([#143](https://github.com/Murmli/MurmliV2/issues/143)) hält niemand eine Sperre auf einem Bereich eines Kontos, das seit mehr als 10 Minuten markiert ist, länger als `Sync:LockTimeout`.

Entschieden, noch nicht gebaut: Metriken und Span des Laufs, mit einem Namen je Beitrag als Tag. Entfällt, sobald die Telemetrie des Abgleichs aus [#138](https://github.com/Murmli/MurmliV2/issues/138) gebaut ist.

## Kontolöschung

`DELETE /v1/users/me` löscht ein Konto samt seinen abgeglichenen Daten und den Verwaltungsdaten des Abgleichs ([#130](https://github.com/Murmli/MurmliV2/issues/130)); alle Schritte samt Löschvermerken und `auth` stehen in [authentication.md](authentication.md#konto-löschen). Für den Abgleich gilt:

1. Als Erstes markiert `SyncAccountLifecycle.MarkDeletingAsync` das Konto als „wird gelöscht“ (`identity.users.deletion_started_at`), in einer eigenen Transaktion unter der Sperre seines persönlichen Bereichs. Von da an antwortet `/v1/sync` mit 410 `account-gone` ([Fehler der ganzen Anfrage](#fehler-der-ganzen-anfrage)): Die API liest die Markierung beim Abruf des Kontos, der Kern in jeder Heads-Anweisung, auch im Schnappschuss des Lesepfads (sonst meldete eine Antwort die schon gelöschten eigenen Bereiche als nicht vom eigenen Konto entfernt), und beim Anlegen eines Wurzel-Datensatzes nach der Sperre des persönlichen Bereichs (Schritt 2 der [Konfliktregeln](#konfliktregeln)). Ein Anlegen, das die Sperre vor der Markierung hielt, ist festgeschrieben, bevor die Markierung greift; sein Bereich wird in Punkt 2 mit den übrigen eigenen Bereichen gelöscht. Einen persönlichen Bereich legt der Kern für ein markiertes Konto nicht mehr an.
2. Danach löscht `SyncAccountLifecycle.DeleteOwnedScopesAsync` die eigenen geteilten Bereiche, je Bereich in einer eigenen Transaktion; Datensätze, Löschmarkierungen und Mitgliedseinträge folgen per `ON DELETE CASCADE`. Löschmarkierungen für die Datensätze braucht es nicht: Fehlende Bereiche erkennen Geräte über ihr Token, und die Geräte des Kontos bekommen ohnehin 410.
3. Erst nach der Löschung in `auth` löscht die API die Zeile in `identity.users`. Der persönliche Bereich, die verarbeiteten Kommandos des Kontos (auch die Löschungen von Wurzel-Datensätzen, die das Aufräumen sonst behält) und was ein Abgleich parallel zur Markierung noch angelegt hat, folgen per `ON DELETE CASCADE`. Mitgliedseinträge des Kontos in fremden Bereichen verlieren per `ON DELETE SET NULL` nur die Verbindung zum Konto ([#131](https://github.com/Murmli/MurmliV2/issues/131)); vor dem Teilen ([#143](https://github.com/Murmli/MurmliV2/issues/143)) gibt es keine.

Bleibt die Löschung nach der Markierung stehen, schließen sie die nächste Anmeldung, ein erneutes `DELETE /v1/users/me` oder nach 10 Minuten der Aufräum-Lauf mit `StaleAccountDeletionContributor` ab ([authentication.md](authentication.md#konto-löschen)).

## Fallsammlung

Die Konfliktregeln liegen zusätzlich als gemeinsame Fallsammlung vor: Ausgangsstand, zwei Kommandos in beiden Reihenfolgen, erwartete Ergebnisse und erwarteter Stand. Die Tests des Backends und die Simulation der App ([#136](https://github.com/Murmli/MurmliV2/issues/136)) lesen dieselben Dateien. Je Fall eine JSON-Datei unter `src/sync-cases/` (JSON, damit keine Seite ein Paket braucht): `core/` mit den künstlichen Datentypen der Tests (`testNote` im persönlichen Bereich, `testBoard` als Wurzel mit `testCard` darin), `shopping/` mit der Einkaufsliste; die Fälle liegen in Unterordnern je Thema (`core/conflicts/`, `shopping/items/`), Dateien direkt im Ordner eines Bereichs sind keine Fälle (etwa `shopping/catalog.json`). Die Simulation liest sie aus `../sync-cases`, von `src/app` aus.

```json
{
  "format": 1,
  "id": "core/conflicts/different-groups-both-stay",
  "rule": "https://github.com/Murmli/MurmliV2/issues/122 Je Feld gewinnt die spätere Zeit",
  "dataTypes": ["testBoard", "testCard"],
  "devices": { "phone": { "account": "alice" }, "tablet": { "account": "alice" } },
  "given": [
    { "device": "phone", "kind": "create", "dataType": "testBoard", "scope": "board", "record": "board", "at": -60000, "fields": { "title": "Groceries" } },
    { "device": "phone", "kind": "create", "dataType": "testCard", "scope": "board", "record": "card", "at": -50000, "fields": { "text": "milk", "done": false } }
  ],
  "race": [
    { "label": "amount", "device": "phone", "kind": "setFields", "dataType": "testCard", "scope": "board", "record": "card", "at": -2000, "fields": { "count": 2, "measure": "liter" } },
    { "label": "done", "device": "tablet", "kind": "setFields", "dataType": "testCard", "scope": "board", "record": "card", "at": -1000, "fields": { "done": true } }
  ],
  "expect": {
    "results": { "amount": { "status": "applied" }, "done": { "status": "applied" } },
    "records": [ { "dataType": "testCard", "record": "card", "fields": { "text": "milk", "done": true, "count": 2, "measure": "liter" } } ],
    "absent": []
  },
  "expectByOrder": {}
}
```

| Feld | Inhalt |
| --- | --- |
| `format` | Version des Formats, derzeit `1` |
| `id` | eindeutiger Name des Falls |
| `rule` | Link auf das Issue der Regel, dahinter ihr Wortlaut |
| `dataTypes` | `dataTypes` jeder Anfrage |
| `devices` | Geräte mit ihrem Konto |
| `given` | Ausgangsstand: Kommandos mit `device`, `kind`, `dataType`, `scope`, `record`, `at` und `fields` |
| `race` | genau zwei Kommandos wie in `given`, zusätzlich mit `label` |
| `expect` | `results`: je `label` das erwartete Ergebnis wie in der Antwort, ohne `commandId` (etwa `{ "status": "ignored", "cause": "lostToNewer" }`); `records`: Liste von `{ dataType, record, fields }`, Datensätze, die es am Ende gibt, verglichen nur in den genannten Feldern; `absent`: Liste von `{ dataType, record }`, Datensätze, die es am Ende nicht geben darf; `record` trägt jeweils einen symbolischen Namen |
| `expectByOrder` | optional, siehe unten |

- Symbolische Namen (`board`, `card`, `alice`) werden je Lauf zu frischen UUIDs bzw. Konten; derselbe Name ergibt in `scope` und `record` dieselbe UUID, bei einem Wurzel-Datensatz sind beide gleich. `$personal` steht für den persönlichen Bereich des Kontos, dessen Gerät das Kommando sendet; seine ID liefert der erste Abgleich des Geräts.
- `at` sind Millisekunden relativ zur Uhr des Servers beim Start des Falls.
- Jedes Kommando geht in einer eigenen Anfrage seines Geräts.
- `given` enthält nur Kommandos, die nacheinander laufen, keine anderen Schritte.
- `race` enthält genau zwei Kommandos mit `label`, der Fall läuft in beiden Reihenfolgen, jede als eigener Lauf samt `given`.
- `expectByOrder` hat höchstens die Schlüssel `"<a>><b>"` und `"<b>><a>"` aus den beiden `label`. Der Schlüssel `"<a>><b>"` heißt: Das Kommando mit `label` a geht zuerst, dann b; `"done>amount"` also erst `done`, dann `amount`. Ein Eintrag hat die Form von `expect` und ersetzt für seine Reihenfolge jedes Mitglied von `expect`, das er nennt (`results`, `records`, `absent`), als Ganzes; die übrigen gelten weiter. Gebraucht wird das, wo gleiche Zeiten die Ergebnisse von der Reihenfolge abhängig machen.
- Den Endstand prüft je Konto ein frisches Gerät mit einem Abgleich ohne Token. Ein Datensatz aus `records` muss in mindestens einer dieser Antworten mit den erwarteten Werten stehen, einer aus `absent` in keiner.
- Eine spätere Version des Formats kann weitere Schritte bringen, wenn ein Fall sie braucht. Grenzen, Sprünge der Uhr und das Aufräumen prüfen eigene Tests mit kleinen Grenzen oder gesteuerter Uhr, keine Fälle.

Im Backend liest `SyncEndpointsCaseCollectionTests` jede Datei in den Unterordnern von `core/` und `shopping/` als eigenen Fall (in `src/backend`: `dotnet test --project Murmli.Backend.Api.Tests --filter-class "*SyncEndpointsCaseCollectionTests"`); das Testprojekt übernimmt `src/sync-cases/` in seine Ausgabe. Der Leser prüft zusätzlich, dass die Datei dem Format genau folgt (unbekannte oder doppelte Felder und fehlende Pflichtfelder lassen den Fall scheitern), dass `id` ihrem Pfad ohne `.json` entspricht und dass jedes Kommando aus `given` `applied` ist.

Entschieden, noch nicht gebaut: der Leser in der Simulation der App. Entfällt, sobald die Simulation aus [#136](https://github.com/Murmli/MurmliV2/issues/136) gebaut ist.

## Testumgebung

Die Tests des Backends laufen gegen ein echtes Postgres (Testcontainers) und `ApiHost` mit derselben Pipeline wie die API. Damit der Kern nichts über die Einkaufsliste weiß, laufen künstliche Datentypen mit, die es nur im Testcode gibt (`Murmli.Backend.Api.Tests/Infrastructure/TestDataTypes/`); ihre Tabellen liegen im Schema `synctest` und entstehen per handgeschriebenem DDL, nie per Migration ([database.md](database.md#abgleich)). Dieselben Datentypen sind für die Simulation der App ([#136](https://github.com/Murmli/MurmliV2/issues/136)) vorgesehen.

Der geplante Aufräum-Lauf ist in den Tests über `ApiHost` standardmäßig aus: `ApiHost` setzt `Sync:Cleanup:Enabled=false`, nur der Test des Zeitplans schaltet ihn ein; diese Tests starten `SyncCleanupRunner.RunAsync` selbst über `ApiHost.Services`. Die Tests in `Murmli.Backend.Aspire.Tests` starten die echte API über den AppHost, dort ist er eingeschaltet. Mit `testCleanupContributors: true` registriert `ApiHost` zwei Beiträge aus dem Testcode: einen, der wirft, und einen, der drei entfernte Datensätze meldet, ohne Daten anzufassen; mit `cleanupContexts` einen, der den Kontext jedes Laufs in die übergebene Liste schreibt; mit `waitingCleanupContributor: true` einen, der bis zur Zeitgrenze des Laufs wartet. Nur Tests mit ihm warten die Zeitgrenze ab, mit `Sync:Cleanup:LeaseDuration=00:02:00` (dem kleinsten erlaubten Wert) eine Minute in Echtzeit.

`ApiHost` hebt die Rate-Limits weit über die Last der Tests an; nur die Tests der Rate-Limits in `SyncEndpointsLimitTests` setzen kleine Werte. Die Rate-Limits laufen in Echtzeit und folgen nicht dem `TimeProvider`; kein Test wartet ein `Retry-After` ab, weil ein Fenster eine Minute dauert.

Als Server schreiben Tests mit `ApiHost.WriteAsServerAsync`, das `SyncServerWriter` in einem eigenen Scope auflöst.

Wie in Produktion legt der erste Abgleich jedes Kontos auch in Tests die erste Einkaufsliste an, weil `AddApi()` ihren Beitrag registriert. Tests, die nur künstliche Datentypen nennen, sehen sie nie; `TestNoteQueries.ReadProcessedCommandsAsync` lässt alle verarbeiteten Kommandos des Datentyps `shoppingList` aus, darunter das der ersten Liste.

Das Testprojekt der API setzt den Laufzeitschalter `System.IO.Compression.UseStrictValidation`, damit eine gzip-Antwort ohne Trailer dort scheitert, statt dass `GZipStream` sie still als vollständig liest; der Schalter muss bleiben.

`testNote` liegt im persönlichen Bereich (`personal`), höchstens 50 je Bereich (`Sync:DataTypes:testNote:MaxRecordsPerScope`):

| Feld | Rolle | Wert |
| --- | --- | --- |
| `title` | eigene Gruppe, Pflicht bei `create` | Text, 0 bis 100 UTF-16-Einheiten |
| `color` | Gruppe `style` mit `weight` | `red`, `green`, `blue` oder `null` |
| `weight` | Gruppe `style` mit `color` | Zahl über 0 bis 100 mit höchstens 1 Nachkommastelle, oder `null` |
| `pinned` | Feld des Servers, an Geräte | `true` oder `false`, beim Anlegen ohne Angabe `false`; setzen darf es nur der Server als Schreiber ([Server als Schreiber](#server-als-schreiber)) |

Regeln von `testNote`, ausgelöst über `title`:

| `title` | Ergebnis |
| --- | --- |
| `rule-reject` | `rejected` mit `invalid` |
| `rule-ignore` | `ignored` mit `lostToNewer` |
| `rule-fail` | `failed`, die Regel wirft eine Exception |

`testBoard` ist die Wurzel geteilter Bereiche der Art `testBoard` (ID gleich Bereichs-ID), höchstens 5 eigene je Konto (`Sync:DataTypes:testBoard:MaxOwnedScopesPerAccount`), ein Datensatz je Bereich (der Wurzel-Datensatz selbst):

| Feld | Rolle | Wert |
| --- | --- | --- |
| `title` | eigene Gruppe, Pflicht bei `create` | Text, 0 bis 100 UTF-16-Einheiten |

Regel von `testBoard`, ausgelöst über `title`: `rule-fail` ergibt `failed`, die Regel wirft eine Exception, auch beim Anlegen des Wurzel-Datensatzes.

`testCard` liegt in einem Bereich der Art `testBoard`, höchstens 50 je Bereich (`Sync:DataTypes:testCard:MaxRecordsPerScope`):

| Feld | Rolle | Wert |
| --- | --- | --- |
| `text` | eigene Gruppe, Pflicht bei `create` | Text, 1 bis 100 UTF-16-Einheiten |
| `count` | Gruppe `amount` mit `measure` | Zahl über 0 bis 1000 mit höchstens 3 Nachkommastellen, oder `null` |
| `measure` | Gruppe `amount` mit `count` | `piece`, `liter` oder `null` |
| `done` | eigene Gruppe | `true` oder `false`, beim Anlegen `false` |
| `changedBy` | Feld des Servers, an Geräte | Mitglieds-ID der Person, deren Änderung zuletzt übernommen wurde, auch beim Anlegen (`ActingMemberOnApply`) |
| `createdBy` | Feld des Servers, nie an Geräte | Mitglieds-ID der Person, die angelegt hat (`ActingMemberOnCreate`), wie der Ersteller eines Artikels |
| `doneBy` | Feld des Servers, an Geräte | Mitglieds-ID der Person, die `done` auf `true` gesetzt hat, bei `false` `null`; gesetzt von der Regel des Datentyps |
| `createdAt` | Feld des Servers, an Geräte | `time` des `create` (`CommandTimeOnCreate`) |

Eine Karte löscht nur der Besitzer (`MinimumRole`).

Bis zu den Vorgängen an der Mitgliedschaft aus [#129](https://github.com/Murmli/MurmliV2/issues/129) legen Tests Mitgliedseinträge weiterer Konten nur über `DatabaseAdministration` an (`InsertMemberEntryAsync`, Rolle `member`, für die Prüfung der Rollen auch `readOnly` und der Zustand `former`), immer zusammen mit der Erhöhung der Version des Bereichs.

### Simulationsumgebung

Gegen diese Umgebung läuft die Simulation der App ([#136](https://github.com/Murmli/MurmliV2/issues/136)): ein Postgres (Testcontainers, `postgres:18`), zwei Instanzen der API an derselben Datenbank und eine Steuer-App, alles im Testprojekt der API, ohne Aspire, Node, `auth` und Azure. Start mit einem Befehl, in `src/backend` (braucht Docker): `dotnet test --project Murmli.Backend.Api.Tests --explicit only --filter-method "*ServeEnvironment_UntilCancelled_KeepsRunning"`.

Der explizite Test `SyncSimulationEnvironmentTests.ServeEnvironment_UntilCancelled_KeepsRunning` schreibt nach dem Start zwei Zeilen `export …` für die Shell der Simulation und hält die Umgebung, bis Strg+C sie beendet; `dotnet test` meldet den Lauf dann als abgebrochen (Exit-Code 3), das ist erwartet. Die Zeilen gehen direkt auf die Konsole, denn Ausgaben über `ITestOutputHelper` zeigt `dotnet test` erst nach dem Ende eines Tests, auch mit `--show-live-output on`. Ohne `--explicit` (Standard `off`) überspringt `dotnet test` den Test, auch in der CI; mit `--explicit on` liefe er neben allen übrigen Tests, und der ganze Lauf wartete auf Strg+C.

| Variable | Richtung | Inhalt |
| --- | --- | --- |
| `SYNC_SIMULATION_API_URLS` | Ausgabe | Adressen beider Instanzen (`http://127.0.0.1:<port>`), durch Komma getrennt |
| `SYNC_SIMULATION_CONTROL_URL` | Ausgabe | Adresse der Steuer-App |
| `SYNC_SIMULATION_RETENTION` | Eingabe, optional | `Sync:Retention` beider Instanzen als `hh:mm:ss`, Standard `01:00:00`, mindestens `00:01:00`; kürzer als ein Lauf, damit `tooOld` und mit `POST /cleanup` die Untergrenze vorkommen |

Beide Instanzen sind `ApiHost` mit den künstlichen Datentypen (Pipeline nur über `UseApi()`) und starten nacheinander: Führen zwei Sitzungen das DDL der Testtabellen gleichzeitig aus, scheitert eine mit 23505. Sie teilen eine Uhr (`FakeTimeProvider`), die beim Start auf der Echtzeit steht (auf Millisekunden gekürzt) und danach stillsteht, bis die Steuer-App sie verstellt. Anders als in den übrigen Tests gelten `Database:MaxRetryCount=10` und `Database:MaxRetryDelay=00:00:00.200`, weil Schnappschuss und Zurücksetzen die Verbindungen im Pool beenden, sowie `Sync:Cleanup:SafetyMargin=00:10:00`, damit bei kurzer Aufbewahrung auch Kommando-IDs aufgeräumt werden, und die Konsole loggt wie in Azure erst ab `Warning`. Den geplanten Aufräum-Lauf schaltet `ApiHost` ab (`Sync:Cleanup:Enabled=false`, [Testumgebung](#testumgebung)), die Simulation startet ihn mit `POST /cleanup`. Die Rate-Limits beider Instanzen liegen wie in allen Tests über `ApiHost` weit über der Last der Simulation ([Testumgebung](#testumgebung)).

Die Steuer-App (`SyncSimulationControlApp`) ist eine eigene Web-App auf `127.0.0.1` im Testcode, nie Teil von `UseApi()`, ohne Anmeldung. Bodys sind JSON (`Content-Type: application/json`), Zeiten ganze Unix-Millisekunden. Fehler haben keinen Body.

| Endpunkt | Body | Antwort |
| --- | --- | --- |
| `GET /environment` | – | `{ "apiUrls": [...], "serverTime": ..., "databaseIdentity": "..." }`: Adressen beider Instanzen, Uhr des Servers, aktuelle Kennung der Datenbank |
| `POST /tokens` | `{ "subject": "user-1" }` | `{ "token": "..." }`: JWT wie aus `TestTokens` mit dem Schlüssel dieses Starts, `sub` gleich `subject`, `iat` aus der Uhr des Servers (auf Sekunden gekürzt), `nbf` und `exp` aus der Echtzeit, eine Stunde gültig; ohne `subject` 400 |
| `POST /clock` | `{ "advanceMilliseconds": n }` oder `{ "setUnixMilliseconds": t }` | `{ "serverTime": ... }`: stellt die gemeinsame Uhr vor oder zurück, `n` darf negativ sein; beide oder keins der Felder oder ein Ziel vor 1970 oder nach dem Jahr 9999 ergeben 400 |
| `POST /cleanup` | – | 204, sobald ein Aufräum-Lauf der ersten Instanz (`SyncCleanupRunner.RunAsync`) zu Ende ist, auch wenn er nichts getan hat: Scheitert ein Schritt, steht `SyncCleanupFailed` auf der Konsole der Instanz; hält ein anderer Lauf die Lease, loggt sie nur `SyncCleanupSkipped` (Information), das ihre Konsole nicht zeigt, weil sie erst ab `Warning` loggt ([Aufbewahrung und Aufräumen](#aufbewahrung-und-aufräumen)) |
| `POST /server-writes` | `{ "subject": "user-1", "command": { "kind": "create", "scopeId": "...", "dataType": "testNote", "recordId": "...", "fields": { "title": "..." } } }` | 200 mit `{ "status": ..., "reason": ..., "cause": ... }` (fehlende Werte `null`): schreibt über `SyncServerWriter.WriteAsync` der ersten Instanz im Namen des Kontos von `subject`, mit einer neuen Kommando-ID und ohne Bedingung ([Server als Schreiber](#server-als-schreiber)); `fields` fehlt bei `delete`. Fehlt `subject`, `command` oder eines von `kind`, `scopeId`, `dataType` und `recordId`, 400; gibt es zu `subject` kein Konto, 404 |
| `POST /snapshots/{name}` | – | 204: beendet die Verbindungen zur Datenbank und kopiert sie per `CREATE DATABASE … TEMPLATE` nach `<datenbank>_<name>`; ein Schnappschuss gleichen Namens wird ersetzt |
| `POST /snapshots/{name}/restore` | – | 204: ersetzt die Datenbank per `DROP DATABASE … WITH (FORCE)` und `CREATE DATABASE … TEMPLATE` durch den Schnappschuss und erneuert danach `sync.database_identity` wie ein Mensch nach dem Einspielen einer Sicherung ([database.md](database.md#abgleich)); ohne diesen Schnappschuss 404, die Datenbank bleibt unverändert |

`name` hat 1 bis 25 Zeichen aus `a` bis `z`, `0` bis `9` und `_`, sonst 400. Schnappschüsse bleiben bis zum Ende der Umgebung im Container.

Ein Konto legt `GET /v1/users/me` mit einem Token der Steuer-App an, vorher antwortet `/v1/sync` mit 410 `account-gone`. `DELETE /v1/users/me` löscht Konten wie in Produktion; anstelle von `auth` bestätigt `AuthServiceHttpMessageHandler` im Testcode jede Löschung. Danach legt ein Token desselben `subject` kein Konto an, solange sein `iat` (ganze Sekunden der gestellten Uhr) nicht nach dem Löschvermerk liegt, aber nur, solange es den Vermerk gibt: Die nächste Kontolöschung, gleich welchen Kontos, entfernt Vermerke, die nach der gestellten Uhr älter als 24 Stunden sind, und das Zurücksetzen auf einen Schnappschuss von vor der Löschung bringt einen Stand ohne den Vermerk zurück. Wie nach einer echten Löschung, nach der Better Auth eine neue ID vergibt, meldet sich die Simulation deshalb mit einem neuen `subject` an. Sobald die gestellte Uhr mehr als 24 Stunden nach einer Kontolöschung steht, ob nach einem großen oder nach mehreren kleinen Schritten, verwirft die Simulation außerdem jedes Token, das vor dieser Löschung für dasselbe `subject` ausgestellt wurde (in Produktion wäre es längst abgelaufen). Tokens gelten nur für die laufende Umgebung, weil der Schlüssel bei jedem Start neu entsteht, und eine Stunde in Echtzeit, unabhängig von der gestellten Uhr. Während Schnappschuss und Zurücksetzen pausiert die Simulation ihren Verkehr: Gegen die gelöschte Datenbank scheitern Anfragen ohne Wiederholung. Danach scheitert die erste Anweisung jeder Instanz an ihrer beendeten Verbindung vorübergehend, Npgsql leert den Pool, und die Execution Strategy verbindet neu.

Entschieden, noch nicht gebaut: die Simulation, die gegen diese Umgebung läuft, samt Befehl für den ganzen Lauf. Entfällt, sobald die Simulation aus [#136](https://github.com/Murmli/MurmliV2/issues/136) gebaut ist.

### Gegenprobe

Eine Simulation, die nie scheitert, beweist nichts. Vor dem ersten Vertrauen in die Simulation und nach größeren Umbauten am Kern werden deshalb nacheinander drei Fehler am Server eingebaut; die Simulation muss jeden beim ersten Startwert finden, das Ergebnis gehört in [#136](https://github.com/Murmli/MurmliV2/issues/136). Die Fehler werden nur lokal eingebaut und nie committet:

1. Geänderte Datensätze fehlen im Abruf: in `SyncDataType<TRecord>.WriteRecordsAsync` (`Murmli.Backend.Sync/DataTypes/SyncDataType.cs`) `record.Version > afterVersion` durch `record.Version > afterVersion + 1` ersetzen.
2. Eine ältere Änderung überschreibt eine neuere: in `SyncDataType<TRecord>.ApplyChanges` die Bedingung `isNew || commandTime >= change.Group.GetChangedAt(typedRecord)` durch `true` ersetzen.
3. Ein wiederholt gesendetes Kommando wird ein zweites Mal ausgeführt: in `SyncCommandUnit.RunAttemptAsync` (`Murmli.Backend.Sync/Processing/SyncCommandUnit.cs`) das Ergebnis von `ClaimAsync` übergehen, statt bei einer vorhandenen Kommando-ID mit `ReadAlreadyProcessedAsync` zu antworten.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Zeitpunkte als ganze Unix-Millisekunden in UTC, Dauern als ganze Sekunden | Keine Zeitzonen beim Lesen, exakte Gleichheit. Verworfen: ISO-8601-Strings (Werte ohne Offset werden als Ortszeit gelesen, unterschiedliche Genauigkeit). |
| Geräte-ID im Header `Murmli-Device-Id` | Die Grenze je Gerät greift, bevor der Body gelesen wird, und das `GET` des Live-Kanals ([#125](https://github.com/Murmli/MurmliV2/issues/125)) nutzt denselben Header. Verworfen: Feld im Body. |
| Rahmen der Anfrage tolerant, Kommandos streng | Ein Rollback der API darf neuere Apps nicht abweisen, deshalb ignoriert der Server unbekannte Felder im Rahmen. Unbekannte Felder eines Kommandos machen nur dieses ungültig, wie [#122](https://github.com/Murmli/MurmliV2/issues/122) es verlangt. Verworfen: überall ignorieren (widerspricht [#122](https://github.com/Murmli/MurmliV2/issues/122)), überall streng samt strengem Lesen des ganzen Bodys (ein einziges fehlerhaftes Kommando würde zur 400 der ganzen Anfrage, die die App endlos wiederholt). |
| Problemtypen als Tag-URI `tag:murmli.de,2026:<code>` in `type` | `type` ist nach RFC 9457 die eigentliche Kennung, Tag-URIs sind für Kennungen gedacht, die sich nicht abrufen lassen. Verworfen: nur der Statuscode (Zwischenstationen und das Framework liefern dieselben), eine Erweiterung `code`, `urn:`- oder `https:`-URIs. |
| Token binär mit fester Breite, base64url | Einfach zu prüfen, deterministisch. Verworfen: JSON-Map (größer, keine feste Reihenfolge), Versionen als LEB128 (etwas kleiner, mehr Parsen). |
| gzip nur für vollständige Stände, gesetzt von der Antwort selbst | Unabhängig von der Umgebung, ohne Wirkung auf den Live-Kanal. Antworten ohne vollständigen Stand bleiben unkomprimiert; hat ein Block den vollständigen Stand, komprimiert der Server auch die Änderungen anderer Bereiche derselben Antwort. Das ist vertretbar, denn BREACH braucht vom Angreifer gesteuerten Text und viele beobachtbare Antworten desselben Opfers; vollständige Stände der Bereiche eines anderen Kontos kann ein Mitglied nicht beliebig auslösen. Verworfen: die Middleware zur Antwortkompression von ASP.NET Core (ihre HTTPS-Prüfung kippt zwischen lokalem HTTP und den weitergereichten Headern in Azure, sie träfe den Ereignisstrom des Live-Kanals und bevorzugt zstd und br). |
| Antwort Block für Block in einen Puffer | Mit gzip liegt ein großer vollständiger Stand nur komprimiert im Speicher, unkomprimiert höchstens ein Block; die Verbindung zur Datenbank ist frei, bevor das Gerät die Antwort lädt. Verworfen: die Antwort erst als Objekte aufbauen (der ganze Stand als Objekte im Speicher), direkt in den Body der Antwort schreiben (Transaktion und Verbindung blieben bis zum Ende des Downloads offen, und nach den ersten gesendeten Bytes ließe sich der Lesepfad nicht mehr wiederholen). |
| `Accept-Encoding` streng gelesen, nur der Eintrag `gzip` zählt | Eine Antwort, die das Gerät nicht entpacken kann, kostet die ganze Runde, eine unkomprimierte liest jedes Gerät; dart:io nennt `gzip` immer ausdrücklich. Verworfen: `*` als gzip werten (RFC 9110 erlaubt es, kein Gerät braucht es), `x-gzip` wie `gzip` werten (RFC 9110 empfiehlt es, kein Gerät sendet es), fehlerhafte Einträge überspringen und den Rest werten. |
| Kein Header für die App-Version, nur der Problemtyp `app-version-unsupported` reserviert | Wie die App ihre Version nennt und welche Versionen gelten, entscheidet [#69](https://github.com/Murmli/MurmliV2/issues/69). Verworfen: Header und Mindestversionen schon jetzt. |
| `settings.maxRequestBytes` bindet das Gerät, die 413 ist nur ein Sicherheitsnetz | Eine 413 mit `Connection: close` kann bei dio als abgebrochene Verbindung ankommen. Verworfen: übergroße Bodys am Server bis zu einer zweiten, harten Grenze zu Ende lesen (eine Grenze mehr, darüber scheitert es trotzdem). |
| Rate-Limits nach dem Abruf des Kontos und vor dem Lesen des Bodys | Jede 429 trägt `UserId`, und ein Gerät in einer Schleife kostet nur den Abruf des Kontos, weder das Parsen des Bodys noch Abfragen des Abgleichs. Kommandos je Konto prüft der Server erst nach dem Lesen, weil erst dann ihre Zahl feststeht. Verworfen: Rate-Limits vor dem Abruf des Kontos (jede 429 ohne `UserId`), alle erst nach dem Parsen des Bodys (bis zu 1 MB JSON je abgewiesener Anfrage). |
| Kommandos je Konto als laufend nachgefüllter Eimer | Ein Konto an der Grenze sendet nach Minuten wieder, nicht erst nach einer Stunde. Eine Minute auf jedes `Retry-After` aufzuschlagen, damit es nie zu kurz ist, verkürzte die Wartezeit insgesamt nicht und verdoppelte sie bei kleinem Fehlbetrag. Verworfen: ein festes Fenster von einer Stunde (bis zu einer Stunde Stillstand). |
| Zähler der Rate-Limits im Speicher jeder Instanz | [#135](https://github.com/Murmli/MurmliV2/issues/135) nimmt hin, dass die Grenzen mit mehreren Instanzen je Instanz gelten; für den Schutz vor Schleifen und Missbrauch genügt das. Verworfen bis [#140](https://github.com/Murmli/MurmliV2/issues/140): Zähler über alle Instanzen. |
| Kern als eigenes Projekt `Murmli.Backend.Sync` in der Solution | Die künstlichen Datentypen der Tests erfüllen den Vertrag der Datentypen aus Testcode, ohne dass Sichtbarkeit für Tests erweitert wird, und die API nutzt ihn über die Grenze ihrer Assembly. Kein eigenes Paket und keine eigene Version ([#121](https://github.com/Murmli/MurmliV2/issues/121): „Keine eigene Bibliothek“ heißt kein eigenes Release). Verworfen: Ordner in der API (Vertrag intern), Prüfung nur über HTTP. |
| Datentypen per Definition mit Ausdrücken (`note => note.Title`) | Spalten stehen nur in der EF-Konfiguration, Typen und Gruppen prüft der Compiler bzw. der Start. Verworfen: Attribute an Entitäten (lägen in Data, das den Kern nicht kennt; Argumente nur Konstanten; `[MaxLength]` änderte die Spaltentypen der Migrationen), Feldgruppen als Complex Types (`item.Name.Value` in jeder Abfrage). |
| Eine Transaktion je Kommando, Claim zuerst | Was vor einer Störung übernommen wurde, bleibt übernommen; ein gleichzeitiges Duplikat wartet auf den Schlüssel und findet danach das Original. Verworfen: eine Transaktion um die ganze Anfrage (ein Retry wiederholte festgeschriebene Kommandos), Claim am Ende. |
| Kein Claim für Kommandos mit unbekannter Art oder unbekanntem Datentyp | In `sync.processed_commands` stehen so nur Arten und Datentypen, die der Server kennt, nie ein String beliebiger Länge, den ein Gerät gewählt hat. Wie ohne lesbare Pflichtfelder bekommt eine Wiederholung wieder `invalid`; kennt ein späterer Server Art und Datentyp, verarbeitet er sie neu, unschädlich, weil das Kommando vorher keine Wirkung hatte. Ein bekannter Datentyp, der nicht in `dataTypes` steht, ist ein fester Wert und bleibt gespeichert. Verworfen: solche Strings bis zu einer Höchstlänge speichern (weiter frei gewählte Inhalte in der Datenbank, eine Grenze ohne fachlichen Grund). |
| Unklarer Commit: Wiederholung über den Claim | `verifySucceeded` liefert immer `false`, die Wiederholung findet die festgeschriebene Kommando-ID und antwortet mit dem gespeicherten Ergebnis. Verworfen: eine Prüfabfrage mit dem berechneten Ergebnis (nicht testbar, falsch, wenn ein Duplikat dieselbe ID inzwischen festgeschrieben hat). |
| Lesen in einem Schnappschuss mit REPEATABLE READ | Die Versionen im Token passen zu den gelieferten Änderungen. Verworfen: Abfragen in READ COMMITTED. |
| Abruf ohne Änderungen mit einer Anweisung | Häufigster Fall ([#123](https://github.com/Murmli/MurmliV2/issues/123)): Abruf des Kontos plus eine Anweisung für Heads, Kennung der Datenbank und Markierung der Kontolöschung; nur die erste Anfrage eines Tages schreibt zusätzlich den Tag der letzten Anfrage. |
| Persönlicher Bereich und erste Liste erst beim ersten Abgleich, in einer Transaktion | Konten von vor dem Abgleich und aus einer überlappenden alten Revision bekommen ihn ohne Migration. Melden sich zwei Geräte gleichzeitig zum ersten Mal an, entsteht genau eine Liste ([#141](https://github.com/Murmli/MurmliV2/issues/141)): Der eindeutige Index des persönlichen Bereichs reiht die ersten Abgleiche, nur der erste schreibt die Liste. Eine gelöschte erste Liste kommt nie wieder, weil der persönliche Bereich bis zur Kontolöschung bleibt. Verworfen: Anlegen in `GET /v1/users/me` (die Anmeldung hinge am Abgleich), in der Transaktion der Kontoanlage, per Backfill-Migration, Anlegen der Liste durch die App (zwei Geräte, zwei Listen). |
| Mitgliedseintrag zwischen Datensatz und Konto | Datensätze nennen Personen über die Mitglieds-ID, nur der Server kennt das Konto dahinter. Beim Verlassen, Entfernen und bei der Kontolöschung ändert sich so eine Zeile je Bereich statt jedes Datensatzes, und kein Mitglied sieht die Konto-ID eines anderen, die über alle Bereiche hinweg gleich wäre. Verworfen: Konto-ID am Datensatz. |
| Eigene Bereiche je Konto unter der Sperre des persönlichen Bereichs gezählt | Zwei gleichzeitige Anlagen überschreiten die Grenze nicht gemeinsam, und Sperren laufen immer in derselben Reihenfolge: erst der persönliche Bereich, dann genau ein weiterer. Verworfen: erst den Zielbereich sperren und den persönlichen nur bei Bedarf. |
| „Vom eigenen Konto entfernt“ aus den angewendeten Löschungen des Wurzel-Datensatzes | Der Kern speichert jedes Kommando samt Ziel und Ergebnis ohnehin, ein partieller Index findet die Löschungen eines Kontos; damit das Kennzeichen nicht mit der Aufbewahrung verfällt, nimmt das Aufräumen diese Löschungen aus (siehe unten). Verworfen: eine eigene Tabelle gelöschter Bereiche. |
| Steuer-App der Simulationsumgebung als eigene Web-App im Testcode | Beide Instanzen bleiben Byte für Byte die Pipeline der API, und Uhr, Tokens, Aufräum-Lauf und Zurücksetzen brauchen in der API keinen Code, den nur Tests aufrufen; den Aufräum-Lauf startet die Steuer-App über den öffentlichen `SyncCleanupRunner`, den in Produktion `SyncCleanupService` aufruft ([#137](https://github.com/Murmli/MurmliV2/issues/137)). Verworfen: Steuer-Endpunkte nach `UseApi()` auf einer der Instanzen (die Instanzen unterschieden sich). |
| Server als Schreiber über dieselbe Einheit wie Kommandos der Geräte | Änderungen des Servers bekommen Claim, Konfliktregeln, Version und Löschmarkierung ohne zweiten Weg, Geräte erfahren sie wie jede andere. `System` speichert die Kommando-ID beim Besitzer des Bereichs, so verschwindet sie mit dessen Konto ([#130](https://github.com/Murmli/MurmliV2/issues/130)). Kommandos des Servers, die schon vor der Verarbeitung als ungültig auffallen, speichert der Kern nicht: Sie sind Fehler im Code und bei jeder Wiederholung gleich (ein `invalid` erst in der Verarbeitung, etwa aus Schritt 7, speichert er wie bei einem Gerät). Einen Wurzel-Datensatz löscht auch der Server nur im Namen des Besitzers: `removedByOwnAccount` stammt aus den gespeicherten Löschungen des Kontos, ein Löschen als `System` oder im Namen eines Mitglieds meldete es sonst dauerhaft dem falschen Konto. Verworfen: Änderungen per eigenem SQL (Geräte erführen sie nie). |
| Tokens der Simulation von der Steuer-App, `iat` aus der Uhr des Servers | Die Simulation braucht weder JWT-Code noch den Schlüssel. `iat` muss der gestellten Uhr folgen, weil der Löschvermerk die Uhr des Servers trägt und mit `iat` verglichen wird ([authentication.md](authentication.md)); mit `iat` aus der Echtzeit legte nach einer Kontolöschung bei zurückgestellter Uhr schon ein älteres Token das Konto wieder an, bei vorgestellter Uhr dagegen kein neues. Verworfen: den Schlüssel an die Simulation geben. |
| Simulationsumgebung als expliziter Test im Testprojekt | Gestellte Uhr, freie Ports, kein `auth` und zwei Instanzen an einer Datenbank, gestartet mit einem Befehl. Verworfen: Ressourcen des AppHost im Run-Mode (Systemuhr, feste Ports, warten auf `auth`, Replikate hinter einem Proxy). |
| Untergrenze und Löschen der Löschmarkierungen in einer Transaktion je Bereich | Bricht das Aufräumen an beliebiger Stelle ab, gilt je Bereich beides oder keins, und kein Gerät bekommt eine Antwort, in der eine Löschung fehlt ([#132](https://github.com/Murmli/MurmliV2/issues/132)). Eine Transaktion je Bereich hält die Sperre kurz, wie bei jedem Vorgang über mehrere Bereiche. Verworfen: erst nach Zeit löschen und die Untergrenze danach anheben. |
| Kommando-IDs nach `LEAST(time, processed_at)` plus Sicherheitsabstand aufräumen | Ein erneut gesendetes Kommando wirkt nie doppelt: Ist die ID eines übernommenen Kommandos entfernt, ist es ohnehin `tooOld`, auch wenn die Uhr des Geräts bis zur Toleranz vorging; ein abgelehntes oder ignoriertes hatte keine Wirkung und wird neu geprüft. IDs von Kommandos weit in der Zukunft bleiben nicht bis zu deren Zeitpunkt. Verworfen: nur nach `time` (solche IDs blieben bis dahin), nur nach `processed_at` (ein Fenster bis zur Toleranz der Uhr, in dem ein Kommando doppelt wirken könnte). |
| Löschmarkierungen und Kommando-IDs vor den Beiträgen, an der Zeitgrenze `SyncCleanupStopped` statt `SyncCleanupCompleted` | Die Schritte des Kerns sind Anweisungen über Portionen ohne Aufrufe anderer Dienste; ein Beitrag kann mit vielen einzelnen Schreibvorgängen oder Aufrufen anderer Dienste die ganze Zeitgrenze aufbrauchen, und die Schritte des Kerns liefen dann in keinem Lauf mehr. Ein Lauf, der an seiner Zeitgrenze endet, meldet das als Warning statt als Abschluss, damit ein Rückstand auffällt, den kein Lauf mehr schafft. Verworfen: Beiträge zuerst mit einem festen Anteil der Zeitgrenze (eine zweite Grenze ohne Grundlage für ihren Wert, die Schritte des Kerns begännen weiter erst danach). |
| Angewendete Löschungen eines Wurzel-Datensatzes bleiben in `sync.processed_commands` bis zur Löschung des Kontos | `removedByOwnAccount` verfällt so nie: Auch ein Gerät, das länger als die Aufbewahrung keinen Abgleich hatte, legt für einen Bereich, den das eigene Konto gelöscht hat, keinen Hinweis ab, wie [#122](https://github.com/Murmli/MurmliV2/issues/122) und [#124](https://github.com/Murmli/MurmliV2/issues/124) es zusagen. Kosten: eine Zeile je gelöschtem geteiltem Bereich, und der Durchgang über die Kommando-IDs läuft auf `ix_processed_commands_retain_from` an diesen Zeilen vorbei. Verworfen: das Kennzeichen mit der Kommando-ID verfallen lassen (schwächte diese Zusage). |
| Kontolöschung: zuerst die Markierung unter der Sperre des persönlichen Bereichs, dann je eigenem Bereich eine Transaktion | Das Anlegen eines Wurzel-Datensatzes liest die Markierung nach derselben Sperre, so legt nach der Markierung kein Kommando mehr einen Bereich an, der danach niemandem gehört ([#130](https://github.com/Murmli/MurmliV2/issues/130)). Eine Transaktion je Bereich folgt der Regel für Vorgänge über mehrere Bereiche ([#123](https://github.com/Murmli/MurmliV2/issues/123)). Das Anlegen liest Markierung und Zahl eigener Bereiche in einer Anweisung, auch wenn der Bereich des Ziels schon besteht: Ein `create` auf einen vorhandenen Bereich ist selten, ein neuer Bereich kostet so keine Anweisung mehr. Verworfen: die Bereiche ohne vorherige Markierung löschen, alle eigenen Bereiche mit einer Anweisung. |
| Markierung auch in den Heads | Ein Schnappschuss zwischen Markierung und dem Löschen der Kontozeile meldete die schon gelöschten eigenen Bereiche sonst als entfernt, nicht vom eigenen Konto. Die Heads-Anweisung liest die Markierung mit einer Unterabfrage, ohne eigene Anweisung, und schon die Heads vor den Kommandos beenden die Anfrage, sodass für ein markiertes Konto kein Kommando mehr läuft. |
| Zeitplan und Lease des Aufräumens nach dem `TimeProvider`, seine Zeitgrenze in Echtzeit | Der Zeitplan lässt sich mit der gestellten Uhr prüfen, und das Ende der Lease ist ein gespeicherter Zeitpunkt wie jeder andere des Abgleichs; in Produktion sind beide Uhren dieselbe. Die Zeitgrenze schützt den Betrieb und folgt deshalb der Echtzeit wie Frist und Budget einer Anfrage. |
| Katalog der Einkaufsliste als Datei in `src/sync-cases/shopping/` und als `ShoppingCatalog` im Code | App und Server lesen dieselben Schlüssel ohne Paket, ein Test prüft, dass der Server jeden Schlüssel der Datei annimmt. Schlüssel in camelCase sind in Daten lesbar. Verworfen: Zahlen als IDs, eine Reihenfolge im Katalog (sie gilt je Person, [#86](https://github.com/Murmli/MurmliV2/issues/86)). |
| Abgehakte Artikel nach der Ankunft am Server entfernen, mit Bedingung unter der Sperre der Liste | Ein Gerät mit weit nachgehender Uhr ließe einen Artikel sonst sofort verschwinden ([#141](https://github.com/Murmli/MurmliV2/issues/141)). Die Bedingung lässt ein Herausnehmen des Hakens gewinnen, das zwischen Abfrage und Löschen ankommt. Verworfen: die Gerätezeit des Abhakens, Löschen per eigenem SQL (Geräte erführen es nie). |
