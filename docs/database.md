# Datenbank

## Schemas

- `better-auth`: die Tabellen von Better Auth mit Namen, E-Mails, Passwort-Hashes, Sessions und den verschlüsselten privaten JWT-Schlüsseln (`database.schemaName` in `src/auth/src/authentication/auth.ts`). In SQL wegen des Bindestrichs immer quotiert: `"better-auth"`.
- `auth` migriert `better-auth` beim Start, bevor es lauscht: `getMigrations` aus `better-auth/db/migration` vergleicht mit dem Live-Schema, ergänzt nur, ist idempotent und wirft bei unsicheren Änderungen. Bringt ein Update von Better Auth eine Pflichtspalte ohne Default, startet die neue Revision nicht. Keine Sperre, weil `auth` höchstens 1 Replica hat, mit mehr braucht es ein Advisory Lock.
- `backend-identity` darf als Entra-Admin Schemas anlegen: `murmli` gehört in Azure `azure_pg_admin`, auf Dev am 2026-11-14 geprüft (`has_database_privilege(..., 'CREATE')` true, `CREATE SCHEMA` läuft).
- `sync`: Tabellen des Sync-Kerns (`scopes`, `processed_commands`, `tombstones`, `database_identity`), geschrieben nur von `Murmli.Backend.Sync`, außer `database_identity` nach dem Einspielen einer Sicherung ([Abgleich](#abgleich)).
- `synctest`: nur in Tests, Tabellen der künstlichen Datentypen, angelegt per handgeschriebenem DDL in `ApiHost`, nie migriert.

## Produktsuche und Extensions

- `products.name` und `products.brand` haben je einen Trigram-Index (`pg_trgm`, GIN mit `gin_trgm_ops`), `Contains` und `ToLower().Contains` nutzen sie nicht.
- Ein ILIKE-Parameter pro Wort lässt Postgres die Indizes kombinieren (`BitmapOr` pro Wort, `BitmapAnd` über die Wörter). Wörter unter 3 Zeichen ergeben keine Trigramme.
- Treffer über den Code laufen über den Primärschlüssel (`code = ANY`, bei UPC-A mit und ohne führende 0) und kommen per `UNION` zu den Worttreffern.
- Azure verlangt jede Extension in `azure.extensions`. Lokal und in Tests gibt es keine Allowlist, ein fehlender Eintrag fällt erst beim ersten Migrieren in Azure auf.

## Abgleich

Regeln des Sync-Kerns ([sync.md](sync.md#aufbau)).

- Einheiten: Eigene Transaktionen hat die API nur im Sync-Kern, jede als Einheit der Execution Strategy der API (`ExecuteInTransactionAsync`, im Lesepfad `ExecuteAsync` mit eigener Transaktion), nie eine um die ganze Anfrage. Je Kommando eine schreibende Einheit in READ COMMITTED, das Anlegen des persönlichen Bereichs in einer eigenen, der Lesepfad in einer Einheit mit REPEATABLE READ (ohne `SET TRANSACTION READ ONLY`). Frist der Anfrage (`Sync:RequestDeadline`) und Zeit für Kommandos (`Sync:CommandPhaseBudget`) laufen in Echtzeit, nie über den `TimeProvider`.
- Claim zuerst: Die erste Anweisung eines Kommandos trägt die Kommando-ID ein, das Ergebnis schreibt dieselbe Transaktion. Ein gleichzeitiges Duplikat wartet auf den Schlüssel und findet danach das Original. `verifySucceeded` liefert immer `false`: Nach einem unklaren Commit wiederholt EF Core die Einheit, und der Claim antwortet mit dem gespeicherten Ergebnis.
- Jede Einheit beginnt jeden Versuch mit `ChangeTracker.Clear()`, `QueryTrackingBehavior.NoTracking` und `Database.AutoSavepointsEnabled = false` (`PrepareSyncUnitAttempt()`); der Pool setzt die beiden Einstellungen bei jeder Ausleihe zurück. `AsTracking()` nur für den gesperrten Bereich und den Zieldatensatz. Alle Schreibzugriffe eines Kommandos nach dem Claim gehen als ein `SaveChangesAsync` hinaus, ein Roundtrip.
- Datenbank-Exceptions fängt eine Einheit nie, ohne sie weiterzuwerfen: Ohne Savepoints bricht ein gescheitertes `SaveChanges` die Transaktion ab, und `CommitAsync` rollt danach ohne Exception still alles zurück. Fehler aus `SaveChanges` kommen in `DbUpdateException` verpackt, `IsDatabaseDisturbance()` und die Prüfung auf 23505 sehen deshalb in die innere Exception. Eigenes SQL wirft `PostgresException` direkt, so auch die 23503 aus Claim und Anlegen des persönlichen Bereichs, wenn das Konto gerade gelöscht wird.
- Was vom Stand unter einer Sperre abhängt, liest eine eigene Anweisung nach der Sperre; in READ COMMITTED nimmt jede Anweisung einen neuen Schnappschuss.
- Gleichzeitig angelegte ID: Schreibt ein anderer Bereich dieselbe Datensatz-ID zuerst fest, scheitert das Einfügen mit 23505 auf dem Primärschlüssel der Tabelle. Der Kern rollt die Einheit zurück und lässt sie einmal neu laufen; der zweite Lauf findet die ID im anderen Bereich (`noAccess`). Jede andere 23505 ergibt `failed`.
- `lock_timeout` (`Sync:LockTimeout`, 5 s) in jeder schreibenden Einheit: Eine hängende Sperre endet mit 55P03 (transient, Retry, dann 503) statt mit wartenden Verbindungen am Pool. Der Wert liegt immer über 0, 0 schaltet die Grenze in Postgres ab.

Eigenes SQL des Kerns, nur feste Anweisungen, Werte immer als Parameter:

| Anweisung | Aufruf | Warum nicht LINQ |
| --- | --- | --- |
| Claim: `INSERT INTO sync.processed_commands … SELECT … FROM (SELECT set_config('lock_timeout', …, true)) AS settings ON CONFLICT (account_id, command_id) DO NOTHING` | `ExecuteSqlAsync`, liefert 1 oder 0 | EF Core kann kein `ON CONFLICT` |
| Sperre: `SELECT * FROM sync.scopes WHERE id = … FOR NO KEY UPDATE` | `FromSql(…).AsTracking().ToListAsync()`, die Zeile danach im Speicher wählen | EF Core kann keine Zeilensperren; ein LINQ-Operator danach (etwa `SingleOrDefaultAsync`) packte die Sperre in eine Unterabfrage |
| Persönlicher Bereich: `INSERT INTO sync.scopes … SELECT … FROM identity.users … ON CONFLICT (owner_account_id) WHERE kind = 'personal' DO NOTHING` | `ExecuteSqlAsync` | bedingtes Einfügen auf einem partiellen eindeutigen Index |
| `SELECT set_config('lock_timeout', …, true)` vor dem Anlegen des persönlichen Bereichs | `ExecuteSqlAsync` | `SET LOCAL` nimmt keinen Parameter |

- Fremdschlüssel: `sync.scopes.owner_account_id` und `sync.processed_commands.account_id` mit `ON DELETE CASCADE` auf `identity.users`; `sync.tombstones.scope_id` und `scope_id` jeder Tabelle eines Datentyps mit `ON DELETE CASCADE` auf `sync.scopes`. `sync.processed_commands.scope_id` hat bewusst keinen Fremdschlüssel: Die Zeile überlebt ihren Bereich, eine Wiederholung antwortet weiter mit dem gespeicherten Ziel.
- Kennung der Datenbank: `sync.database_identity` hat genau eine Zeile (`HasData` mit fester Startkennung, `CHECK (singleton)`). Der Kern liest sie bei jeder Anfrage in der Heads-Anweisung, nie zwischengespeichert. Nach dem Einspielen einer Sicherung erneuert sie ein Mensch mit `UPDATE sync.database_identity SET value = gen_random_uuid();`, danach bekommt jedes Gerät den vollständigen Stand ([sync.md](sync.md#token)).
- Künstliche Datentypen in Tests: `ApiHost` mit `testDataTypes: true` ersetzt nach `AddApi()` per `ConfigureDbContext` nur den `IModelCustomizer` (`SyncTestModelCustomizer`, Lebensdauer Singleton, nur per Typ, nie mit Instanzen je Host, sonst wirft EF Core ab 20 internen Service-Providern) und führt nach der Migration das DDL für `synctest` aus. Die Testtabellen sind `ExcludeFromMigrations()`, die Prüfung auf ausstehende Modelländerungen bleibt in jedem Host aktiv.

## Lokal

Postgres-Container ohne Volume (Daten überleben keinen Neustart des AppHost) auf festem Host-Port 60123, damit DataGrip und Co. eine stabile Verbindung behalten. User `postgres`, Passwort in den User Secrets des AppHost (`Parameters:postgres-password`). Aspire-Tests vergeben den Port zufällig und laufen auch neben einem gestarteten AppHost.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Raw SQL mit Schema | `search_path` enthält nur `public`. |
| Keine Optionen in `OnConfiguring` | Die Aspire-Integration (`AddAzureNpgsqlDbContext`) poolt den Context, EF wirft sonst `PoolingOptionsModified`. Verworfen: `EFCore.NamingConventions`. |
| EF Core 10.x bis [#14](https://github.com/Murmli/MurmliV2/issues/14) | Aspires EF-Integration 13.6.5 hängt am Npgsql-Provider 10, der für EF Core 11 ist nur RC. Pakete der 11er-Linie (auch `Microsoft.EntityFrameworkCore.Design`) brechen den Restore. |
| Direkte Referenz auf `Microsoft.EntityFrameworkCore.Relational` | Die Aspire-Integration allein zöge eine ältere Patch-Version als die der Migrationen. |
| `MurmliDbContextDesignTimeFactory` | Die Aspire-Registrierung holte zur Design-Zeit ein Azure-Token. |
| Migration beim Start von API und Job | Neue Tabellen gibt es direkt nach dem Deploy. Kestrel lauscht erst danach, bis dahin bedient die alte Revision, auch gegen das migrierte Schema (deshalb additiv und nullable). EF Core sperrt selbst (`LOCK TABLE` auf `__EFMigrationsHistory`), ein zweiter Prozess wartet. Die Migration der API läuft vor dem Host-Start und erzeugt nur Logs, keine Spans. Verworfen: eigener Migrationsjob bzw. Migration-Bundle (zusätzliche Ressource), Aspires EF-Migrations-Integration (nur Preview). |
| Restrisiko beim gleichzeitigen Start von API und Job | Stehen mehrere Migrationen aus, kann ein Prozess eine vom anderen schon angewandte erneut anwenden (EF Core liest die Historie zwischen den Migrationen nicht neu) und scheitert beim Start. Ein Neustart bzw. Retry des Jobs heilt das. |
| Squash nur durch einen Menschen | Ein neues `InitialCreate` passt nur auf eine zurückgesetzte Datenbank. Vor dem ersten Live-Gang auf Dev vertretbar, bricht „additiv und nullable“ bewusst. |
| Eigene Transaktionen nur als Einheit der Execution Strategy, je Kommando eine | Die Retry-Strategie wiederholt nur ganze Einheiten und verträgt keine selbst gestarteten Transaktionen außerhalb davon. Eine Einheit um die ganze Anfrage wiederholte nach einem Retry schon festgeschriebene Kommandos, verschachtelte Strategien wiederholen nicht. Der Produktimport schaltet die Strategie weiter ab. |
| Retry-Budget der API | Höchstens 2 Retries mit höchstens 2 s Verzögerung (`Database:MaxRetryCount`, `Database:MaxRetryDelay`), gesetzt in `AddApi()` über `AddMurmliDbContext(configureNpgsql: ...)` aus `IOptions<DatabaseOptions>`. Gelesen werden die Werte erst beim Aufbau der Context-Optionen, also beim ersten Context: Die Migration läuft vor dem Host-Start und damit vor `ValidateOnStart`, so lehnt die Prüfung der Optionen ungültige Werte trotzdem vor der ersten Verbindung ab. Aspires Standard von 6 Retries dauert etwa eine Minute, länger als der Timeout der App von 60 s: Geräte bekämen nie die 503 mit Wartezeit, und der Abruf der Konto-Id hielte Pool-Verbindungen eine Minute lang. Der Produktimport behält `DisableRetry`. Verworfen: Aspires Standard plus Deadline, weil der Abruf der Konto-Id ohne Cancellation läuft und unbegrenzt bliebe; die Werte per `Configuration.Get<DatabaseOptions>()` in `AddApi()` lesen (ungeprüft bis nach der Migration); alle Startprüfungen vor der Migration ausführen (die Werte kämen weiter aus einer zweiten, ungeprüften Bindung). |
| Smart Enums per `HasConversion` | Verworfen: Thinktecture-EF-Paket, es ist an eine EF-Major gebunden. |
| `azure.extensions` nur aus `Database/Extensions.cs` | Der AppHost setzt den Parameter bei jedem Deploy vollständig, Pflege im Portal ginge verloren. |
| `DependsOn` an Datenbank und Firewall-Regel | Der Server lehnt parallele Änderungen an seinen Child-Ressourcen ab. |
| Default-Collation, keine eigene | Umlaute sortieren womöglich nicht nach deutschen Regeln, akzeptiert. |
| Sync-Kern über EF Core, eigenes SQL nur für feste Anweisungen auf `sync.*` und `identity.users` | „Datenzugriff mit EF Core“ gilt auch für den Kern, Spalten stehen nur in der EF-Konfiguration. Preis: im persönlichen Bereich 5 (Felder setzen, Löschen) bis 7 (Anlegen) Roundtrips je Kommando statt 3, weil EF Core keine Abfragen bündelt. Verworfen: vom Kern erzeugtes SQL für jede Tabelle eines Datentyps (breite Ausnahme, Spalten an zwei Stellen), Datensätze per SQL aus EF-Metadaten, ein zweiter `DbContext` nur für Tests. |
| Gleichzeitig angelegte ID per 23505 und zweitem Lauf | EF Core kann kein `ON CONFLICT`; dieselbe Form wie `UserRegistry.GetOrCreateAsync`. Verworfen: automatische Savepoints (je `SaveChanges` ein Roundtrip mehr), `pg_advisory_xact_lock` je Anlegen, eigenes SQL je Tabelle. |
| Lesepfad ohne `SET TRANSACTION READ ONLY` | Eine Transaktion mit REPEATABLE READ, die nur liest, scheitert nie an Serialisierung; die Anweisung kostete einen Roundtrip ohne Gewinn. |
| Kennung der Datenbank als Tabelle mit einer Zeile, bei jeder Anfrage gelesen | Zwei Instanzen und ein Zurücksetzen in Tests sehen eine Erneuerung sofort, und die Systemkennung des Clusters überlebt das Einspielen eines Base-Backups. Verworfen: Cache im Prozess, Parameter im AppHost. |
