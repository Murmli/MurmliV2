# Datenbank

`src/backend/Murmli.Backend.Data` enthält `MurmliDbContext`, die Entities und die Migrationen. `AddMurmliDbContext()` registriert den Context über die Aspire-Integration `AddAzureNpgsqlDbContext` (Verbindung `murmli`, Entra-Anmeldung), die ihn poolt.

## Schemas

- `identity`: Nutzerkonten mit Zuordnung zur Better-Auth-ID (`auth_user_id`) und Löschvermerke.
- `products`: Produkte und Importläufe.
- `better-auth`: die Tabellen von Better Auth (`user`, `session`, `account`, `verification`, `jwks`) mit Name und E-Mail der Nutzer, Passwort-Hashes, Sessions und den verschlüsselten privaten JWT-Schlüsseln. Das Schema gehört Better Auth (`database.schemaName` in `src/auth/src/auth.ts`), EF Core und die API greifen nie darauf zu (siehe [authentication.md](authentication.md#entscheidungen)). Wegen des Bindestrichs in eigenem SQL immer quotiert: `"better-auth"`.
- `auth` migriert `better-auth` beim Start selbst, bevor es lauscht: `getMigrations` aus `better-auth/db/migration` vergleicht mit dem Live-Schema, ergänzt nur, ist idempotent und wirft bei unsicheren Änderungen. Ein Update von Better Auth kann so beim Start Spalten ergänzen, eine neue Pflichtspalte ohne Default verhindert den Start der neuen Revision. Es gibt keine Sperre, weil `auth` höchstens 1 Replica hat, mit mehr Replicas braucht es ein Advisory Lock.
- `backend-identity` darf als Entra-Admin Schemas anlegen: `murmli` gehört in Azure `azure_pg_admin`, auf Dev am 2026-11-14 geprüft (`has_database_privilege(..., 'CREATE')` true, `CREATE SCHEMA` läuft).

## Produktsuche und Extensions

- `products.name` und `products.brand` haben je einen Trigram-Index (`pg_trgm`, GIN mit `gin_trgm_ops`). `Contains` und `ToLower().Contains` nutzen sie nicht.
- Jedes Wort des Suchbegriffs ist ein eigener ILIKE-Parameter auf Name oder Marke, so kombiniert Postgres die Indizes (`BitmapOr` pro Wort, `BitmapAnd` über die Wörter). Wörter unter 3 Zeichen ergeben keine Trigramme und filtern nur, deshalb braucht mindestens ein Wort 3 Zeichen.
- Treffer über den Code laufen über den Primärschlüssel (`code = ANY`), bei UPC-A mit und ohne führende 0, und kommen per `UNION` zu den Worttreffern.
- Azure verlangt jede Extension im Serverparameter `azure.extensions`.
- Lokal und in Tests gibt es keine Allowlist, ein fehlender Eintrag fällt erst beim ersten Migrieren in Azure auf.

## Lokal

Postgres-Container ohne Volume auf Host-Port 60123, User `postgres`, Passwort in den User Secrets des AppHost (`Parameters:postgres-password`). Die Daten überleben keinen Neustart des AppHost. Aspire-Tests vergeben den Port zufällig und laufen deshalb auch neben einem gestarteten AppHost.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Raw SQL mit Schema | `search_path` enthält nur `public`. |
| Keine Optionen in `OnConfiguring` | Die Aspire-Integration poolt den Context, EF wirft sonst `PoolingOptionsModified`. Deshalb snake_case explizit in den Konfigurationen. Verworfen: `EFCore.NamingConventions`. |
| EF Core 10.x | Aspires EF-Integration 13.6.5 hängt am Npgsql-Provider 10, der Provider für EF Core 11 ist nur als RC verfügbar, Pakete der EF-Core-11-Linie (auch `Microsoft.EntityFrameworkCore.Design` 11) brechen den Restore. Gilt bis [#14](https://github.com/Murmli/MurmliV2/issues/14). |
| Direkte Referenz auf `Microsoft.EntityFrameworkCore.Relational` | Alle Projekte nutzen dieselbe EF-Version wie die Migrationen, die Aspire-Integration allein zöge eine ältere Patch-Version. |
| `MurmliDbContextDesignTimeFactory` | Die Aspire-Registrierung würde zur Design-Zeit ein Azure-Token holen. |
| Migration beim Start von API und Job | Neue Tabellen gibt es direkt nach dem Deploy. Kestrel lauscht erst nach der Migration, die alte Revision bedient so lange weiter. EF Core sperrt Migrationen selbst (`LOCK TABLE` auf `__EFMigrationsHistory`), ein zweiter Prozess wartet. Weil die alte Revision gegen das migrierte Schema weiterläuft, bleiben Migrationen additiv und nullable. Die Migration der API läuft vor dem Host-Start und erzeugt deshalb nur Logs, keine Spans. Verworfen: eigener Migrationsjob bzw. Migration-Bundle, er bräuchte eine zusätzliche Ressource, Aspires EF-Migrations-Integration gibt es nur als Preview. |
| Restrisiko beim gleichzeitigen Start von API und Job | Stehen mehrere Migrationen aus, kann der zuerst gestartete Prozess eine vom anderen schon angewandte Migration erneut anwenden, weil EF Core die Historie nach jeder Migration nicht neu liest. Er scheitert dann mit einem Fehler beim Start, ein Neustart (Container App bzw. Retry des Jobs) heilt das. |
| In der API keine eigenen Transaktionen und kein eigenes SQL | Die Retry-Strategie der Aspire-Integration (standardmäßig an) verträgt keine selbst gestarteten Transaktionen, nur der Produktimport schaltet sie dafür ab. Datenzugriff läuft über EF Core, eigenes SQL nur, wo EF Core es nicht kann (etwa `COPY` im Produktimport). |
| Smart Enums per `HasConversion` | Verworfen: Thinktecture-EF-Paket, es ist an eine EF-Major gebunden. |
| `azure.extensions` nur aus `Database/Extensions.cs` | Der AppHost setzt den Parameter bei jedem Deploy vollständig. Verworfen: Pflege im Portal, sie geht beim Deploy verloren. |
| `DependsOn` an Datenbank und Firewall-Regel | Der Server lehnt parallele Änderungen an seinen Child-Ressourcen ab. |
| Fester Host-Port 60123 (`WithHostPort`) | DataGrip und andere Tools behalten eine stabile Verbindung. |
| Sortierung nach der Default-Collation, keine eigene Collation | Umlaute sortieren womöglich nicht nach deutschen Regeln, das ist akzeptiert. |
