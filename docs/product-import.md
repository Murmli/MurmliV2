# Produktimport

`src/backend/Murmli.Backend.ProductImport` enthält die Importlogik, `src/backend/Murmli.Backend.ProductImport.Host` startet sie als Worker ohne HTTP-Endpoints. Der Import streamt den Open-Food-Facts-Export (`openfoodfacts-products.jsonl.gz`, ca. 14 GB gzip, ca. 86 GB JSONL) ohne Zwischenspeicher auf der Platte, filtert deutsche Produkte (`countries_tags` enthält `en:germany`) und gleicht sie in einer Transaktion mit `products` ab (`MERGE … WHEN NOT MATCHED BY SOURCE THEN DELETE`).

## Betrieb

- AppHost-Ressource `product-import`, publiziert als Container-Apps-Job: täglich 08:00 UTC (Cron in UTC, OFF lädt den Export gegen 05:30 UTC hoch), 0,5 vCPU, 1 GiB, `ReplicaTimeout` 7200 s, ein Retry.
- `WithExplicitStart` verhindert, dass `aspire run` und `dotnet test` den Export laden. Lokal startet man ihn über das Dashboard oder `aspire resource product-import start`, er lädt dann den vollen Export wie in Azure.
- Der AppHost reicht `ProductImport__ProductLimit` nur im Run-Mode an `product-import` weiter. Ein Lauf mit erreichtem Limit löscht nichts.
- Derzeit migriert nur der Job (`MigrateAsync` beim Start).

## Nährwerte im Export

| | Ab `schema_version` 1003 | Ältere Produkte |
| --- | --- | --- |
| Quelle | `nutrition.aggregated_set.nutrients.<key>.value`, Keys ohne `_100g`, `per` ist `100g` oder `100ml`, nur bei `preparation` `as_sold` gelesen | `nutriments.<key>_100g`, kJ meist nur als `energy_100g` |
| `nutriments` | leer | gefüllt |

- Geschätzt ist ein Wert nur bei `source` `estimate`. Von OFF umgerechnete Packungswerte (`modifier` `~`) und Obergrenzen (`<`) gelten als Packungswert.
- Bei `schema_version` über 1004 loggt der Reader eine Warnung.

## Importläufe (`product_import_runs`)

- Start, Ende, Status `running`, `succeeded` oder `failed`, kein Fehlertext (Fehler gehen ins Logging).
- `peak_memory_bytes`: Spitzenwert des Arbeitsspeichers des Prozesses (`Process.PeakWorkingSet64`, unter Linux VmHWM), bei erfolgreichen und fehlgeschlagenen Läufen, nach einem harten Abbruch und auf Plattformen ohne Spitzenwert (macOS) leer.
- Der Lauf wird vor dem Download als `running` committet. `succeeded` landet in derselben Transaktion wie der `MERGE`, Daten und Status werden nur gemeinsam sichtbar.
- Ein harter Abbruch (Timeout, Speicher, Plattformwartung) hinterlässt `running`, der nächste Lauf setzt solche Läufe auf `failed` und lässt das Ende leer.
- Parallele Läufe sind nicht vorgesehen.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `ReplicaTimeout` 7200 s | Aspires Standard von 1800 s gilt inklusive Retries und reicht für den Export nicht. |
| `ProductImport__ProductLimit` nie global | Global gesetzt schlägt ein Aspire-Test fehl. |
| `value` statt `value_computed` | `value_computed` ist nicht auf 100 g umgerechnet. |
| Puffern im RAM, Schreiben nach dem Lesen | Die Transaktion bleibt kurz. Verworfen: COPY während des Downloads, er hielte die Transaktion 30–60 Minuten offen. |
| `DisableRetry` | Die Retry-Strategie der Aspire-Integration kollidiert mit der eigenen Transaktion. |
| `System.IO.Compression.UseStrictValidation` in `Murmli.Backend.ProductImport.Host` | `GZipStream` behandelt abgeschnittene Daten sonst still als Dateiende, der Abgleich würde dann Produkte löschen. Verworfen: `AppContext.SetSwitch`, die Runtime liest den Wert beim ersten Gebrauch und cached ihn. |
| Option auch im Testprojekt | `RuntimeHostConfigurationOption` gilt nur für den startenden Host. Ein Test prüft die runtimeconfig des Host-Projekts. |
| Leerer Export ist ein Fehler | Ein leerer Body ist für `GZipStream` kein Fehler, deshalb wirft der Reader selbst. |
| Kein Resilience-Handler am Download-`HttpClient` | Ein Retry könnte den Stream nicht fortsetzen, ein abgebrochener Download lässt den Lauf scheitern. |
| Lese-Timeout 2 Minuten (`TimeoutException`, Timer pro Lesevorgang über `TimeProvider`) | `HttpClient.Timeout` gilt nur bis zu den Headern, ein Hänger würde den Lauf sonst nie beenden. |
| Eigenes COPY + `MERGE` | Verworfen (geprüft 2026-11-14): `Thinktecture.EntityFrameworkCore.PostgreSQL`. Version 10.5.0 hat für PostgreSQL weder MERGE noch ein Löschen fehlender Zeilen, ihr Upsert (`ON CONFLICT DO UPDATE` ohne Änderungsprüfung) schrieb im Test mit 450.000 Produkten jede Nacht alle Zeilen neu. Der Nachbau mit dem Query-basierten Bulk-Update verlor in 10.5.0 still Übergänge zwischen NULL und Wert und war nicht schneller (Folgelauf 2,7 s gegenüber 1,9 s). Gilt, bis 10.6.0 stabil ist und Temp-Tabellen oder Bulk-Inserts auch an anderer Stelle gebraucht werden. |
| Migrationen an `products` additiv und nullable | Solange ein älterer Job noch laufen kann, scheitert sonst dessen COPY oder `MERGE`. |
