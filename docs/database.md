# Datenbank

## Schemas

- `better-auth`: die Tabellen von Better Auth mit Namen, E-Mails, Passwort-Hashes, Sessions und den verschlüsselten privaten JWT-Schlüsseln (`database.schemaName` in `src/auth/src/authentication/auth.ts`). In SQL wegen des Bindestrichs immer quotiert: `"better-auth"`.
- `auth` migriert `better-auth` beim Start, bevor es lauscht: `getMigrations` aus `better-auth/db/migration` vergleicht mit dem Live-Schema, ergänzt nur, ist idempotent und wirft bei unsicheren Änderungen. Bringt ein Update von Better Auth eine Pflichtspalte ohne Default, startet die neue Revision nicht. Keine Sperre, weil `auth` höchstens 1 Replica hat, mit mehr braucht es ein Advisory Lock.
- `backend-identity` darf als Entra-Admin Schemas anlegen: `murmli` gehört in Azure `azure_pg_admin`, auf Dev am 2026-11-14 geprüft (`has_database_privilege(..., 'CREATE')` true, `CREATE SCHEMA` läuft).

## Produktsuche und Extensions

- `products.name` und `products.brand` haben je einen Trigram-Index (`pg_trgm`, GIN mit `gin_trgm_ops`), `Contains` und `ToLower().Contains` nutzen sie nicht.
- Ein ILIKE-Parameter pro Wort lässt Postgres die Indizes kombinieren (`BitmapOr` pro Wort, `BitmapAnd` über die Wörter). Wörter unter 3 Zeichen ergeben keine Trigramme.
- Treffer über den Code laufen über den Primärschlüssel (`code = ANY`, bei UPC-A mit und ohne führende 0) und kommen per `UNION` zu den Worttreffern.
- Azure verlangt jede Extension in `azure.extensions`. Lokal und in Tests gibt es keine Allowlist, ein fehlender Eintrag fällt erst beim ersten Migrieren in Azure auf.

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
| In der API keine eigenen Transaktionen | Die Retry-Strategie der Aspire-Integration (standardmäßig an) verträgt keine selbst gestarteten Transaktionen. Nur der Produktimport schaltet sie dafür ab. |
| Smart Enums per `HasConversion` | Verworfen: Thinktecture-EF-Paket, es ist an eine EF-Major gebunden. |
| `azure.extensions` nur aus `Database/Extensions.cs` | Der AppHost setzt den Parameter bei jedem Deploy vollständig, Pflege im Portal ginge verloren. |
| `DependsOn` an Datenbank und Firewall-Regel | Der Server lehnt parallele Änderungen an seinen Child-Ressourcen ab. |
| Default-Collation, keine eigene | Umlaute sortieren womöglich nicht nach deutschen Regeln, akzeptiert. |
