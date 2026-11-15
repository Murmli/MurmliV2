# Datenbank

`src/backend/Murmli.Backend.Data` enthält `MurmliDbContext`, die Entities und die Migrationen. `AddMurmliDbContext()` registriert den Context über die Aspire-Integration `AddAzureNpgsqlDbContext` (Verbindung `murmli`, Entra-Anmeldung), die ihn poolt.

## Schemas

- `identity`: Nutzerkonten und Löschvermerke.
- `products`: Produkte und Importläufe.
- `backend-identity` darf als Entra-Admin Schemas anlegen: `murmli` gehört in Azure `azure_pg_admin`, auf Dev am 2026-11-14 geprüft (`has_database_privilege(..., 'CREATE')` true, `CREATE SCHEMA` läuft).

## Namenssuche und Extensions

- `products.name` hat einen Trigram-Index (`pg_trgm`, GIN mit `gin_trgm_ops`). `Contains` und `ToLower().Contains` nutzen ihn nicht.
- Azure verlangt jede Extension im Serverparameter `azure.extensions`.
- Lokal und in Tests gibt es keine Allowlist, ein fehlender Eintrag fällt erst beim ersten Migrieren in Azure auf.

## Lokal

Postgres-Container auf Host-Port 60123, User `postgres`, Passwort in den User Secrets des AppHost (`Parameters:postgres-password`). Aspire-Tests vergeben den Port zufällig und laufen deshalb auch neben einem gestarteten AppHost.

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
