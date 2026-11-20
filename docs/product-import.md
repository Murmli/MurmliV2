# Produktimport

`Murmli.Backend.ProductImport` (Logik) und `Murmli.Backend.ProductImport.Host` (Worker ohne HTTP) streamen den Open-Food-Facts-Export (`openfoodfacts-products.jsonl.gz`, ca. 14 GB gzip, ca. 86 GB JSONL) ohne Zwischenspeicher auf der Platte, filtern deutsche Produkte (`countries_tags` enthält `en:germany`) und gleichen sie in einer Transaktion mit `products` ab. Produkte, die im Export fehlen, löscht der `MERGE` (`WHEN NOT MATCHED BY SOURCE THEN DELETE`).

## Betrieb

- Container-Apps-Job `product-import`, täglich 08:00 UTC, weil OFF den Export gegen 05:30 UTC hochlädt.
- `WithExplicitStart`, damit `aspire run` und `dotnet test` den Export nicht laden. Ein lokaler Start ohne `ProductImport__ProductLimit` lädt den vollen Export.
- `ProductImport__ProductLimit` reicht der AppHost nur im Run-Mode weiter. Ein Lauf mit erreichtem Limit löscht nichts.
- Jeder Lauf ist ein Trace `product-import run` mit Migration, Download und Datenbankarbeit. Ein fehlgeschlagener Lauf trägt Fehlerstatus und Exception und wird als Fehler geloggt (Event 1005).

## Übernommene Felder

- Name: `product_name_de`, sonst `product_name_en`, sonst `product_name`.
- Allgemeine Bezeichnung: `generic_name_de`, sonst `generic_name` (Hauptsprache des Produkts).
- Marke: `brands` als ein Text, wie geliefert (z.B. `ja!, Rewe, ja! Rewe`).
- Beliebtheit: `popularity_key` (weltweite Scans), kommt auch als Text, alles außer einer Zahl gilt als fehlend.
- Code, Name, Marke und Bezeichnung: `\0` entfernt, Leerraum am Rand abgeschnitten, leer gilt als fehlend. Ohne Code oder Name wird das Produkt übersprungen, ohne Marke oder Bezeichnung nicht (NULL).

## Nährwerte im Export

| | Ab `schema_version` 1003 | Ältere Produkte |
| --- | --- | --- |
| Quelle | `nutrition.aggregated_set.nutrients.<key>.value`, Keys ohne `_100g`, `per` ist `100g` oder `100ml`, gelesen nur bei `preparation` `as_sold` | `nutriments.<key>_100g`, kJ meist nur als `energy_100g` |
| `nutriments` | leer | gefüllt |

- Geschätzt ist ein Wert nur bei `source` `estimate`. Von OFF umgerechnete Packungswerte (`modifier` `~`) und Obergrenzen (`<`) gelten als Packungswert.

## Importläufe (`product_import_runs`)

- Kein Fehlertext, Fehler gehen ins Logging.
- `peak_memory_bytes`: `Process.PeakWorkingSet64` (unter Linux VmHWM), leer nach einem harten Abbruch und auf macOS.
- Der Lauf wird vor dem Download als `running` committet, `succeeded` in derselben Transaktion wie der `MERGE`: Daten und Status werden nur gemeinsam sichtbar.
- Ein harter Abbruch (Timeout, Speicher, Plattformwartung) hinterlässt `running`, der nächste Lauf setzt ihn auf `failed` und lässt das Ende leer.
- Parallele Läufe sind nicht vorgesehen.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| `ReplicaTimeout` 7200 s | Aspires Standard von 1800 s gilt inklusive Retries und reicht nicht. |
| `ProductImport__ProductLimit` nie global | Sonst schlägt ein Aspire-Test fehl. |
| `value` statt `value_computed` | `value_computed` ist nicht auf 100 g umgerechnet. |
| Puffern im RAM, Schreiben nach dem Lesen | Die Transaktion bleibt kurz. Verworfen: COPY während des Downloads, die Transaktion stünde 30–60 Minuten offen. |
| `DisableRetry` | Die Retry-Strategie der Aspire-Integration kollidiert mit der eigenen Transaktion. |
| `System.IO.Compression.UseStrictValidation` im Host- und im Testprojekt | Sonst behandelt `GZipStream` abgeschnittene Daten still als Dateiende, und der Abgleich löscht Produkte. `RuntimeHostConfigurationOption` gilt nur für den startenden Host. Verworfen: `AppContext.SetSwitch`, die Runtime cached den Wert beim ersten Gebrauch. |
| Leerer Export ist ein Fehler | Für `GZipStream` ist ein leerer Body kein Fehler, deshalb wirft der Reader selbst. |
| Kein Resilience-Handler am Download-`HttpClient` | Ein Retry könnte den Stream nicht fortsetzen, ein abgebrochener Download lässt den Lauf scheitern. |
| Lese-Timeout 2 Minuten pro Lesevorgang | `HttpClient.Timeout` gilt nur bis zu den Headern, ein Hänger beendete den Lauf sonst nie. |
| Eigenes COPY + `MERGE` | Verworfen (geprüft 2026-11-14): `Thinktecture.EntityFrameworkCore.PostgreSQL` 10.5.0. Kein MERGE und kein Löschen fehlender Zeilen, der Upsert (`ON CONFLICT DO UPDATE` ohne Änderungsprüfung) schrieb im Test mit 450.000 Produkten jede Nacht alle Zeilen neu. Der Nachbau mit dem Query-basierten Bulk-Update verlor still Übergänge zwischen NULL und Wert und war nicht schneller (Folgelauf 2,7 s statt 1,9 s). Gilt, bis 10.6.0 stabil ist und Temp-Tabellen oder Bulk-Inserts auch an anderer Stelle gebraucht werden. |
| Marke als Text statt Liste | `brands_tags` ist uneinheitlich (`ja`, `Rewe`, `ja! Rewe`). Eine Liste lohnt erst mit einem Filter nach Marke und braucht dann eine Bereinigung. |
| Beliebtheit aus `popularity_key` | Open Food Facts sortiert selbst danach. Ein Signal nur für Deutschland gibt es nur grob über `popularity_tags` (`top-country-de-scans-<Jahr>`). |
