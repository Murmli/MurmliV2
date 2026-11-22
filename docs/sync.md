# Abgleich

Vertrag zwischen App und API für den Abgleich ([#122](https://github.com/Murmli/MurmliV2/issues/122)), Aufbau und Begriffe in [#121](https://github.com/Murmli/MurmliV2/issues/121). Ein einziger Endpunkt nimmt die wartenden Kommandos eines Geräts an und liefert in derselben Antwort, was sich seit dessen Token geändert hat; ein neuer Datentyp braucht keinen neuen Endpunkt. Der Server entscheidet: Das Gerät wendet eigene Änderungen sofort an, Konfliktregeln gibt es nur am Server. Er liefert den aktuellen Stand geänderter Datensätze statt Änderungsfolgen, ein vollständiger Stand repariert jede Abweichung. Jede Anfrage darf beliebig oft wiederholt werden, ohne dass etwas doppelt wirkt, und kein Kommando hält die Warteschlange auf Dauer auf.

Entschieden, noch nicht gebaut: alles in diesem Dokument. Entfällt, sobald kein Teil mehr einen eigenen Vermerk dieser Art trägt; jeder Vermerk nennt, wann er entfällt.

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

## Vertrag

Entschieden, noch nicht gebaut: die Seite des Servers in diesem Kapitel samt der Prüfung der Untergrenze, soweit kein eigener Vermerk ein anderes Issue nennt. Entfällt, sobald der Sync-Kern aus [#123](https://github.com/Murmli/MurmliV2/issues/123) gebaut ist.

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

Jedes Konto hat genau einen persönlichen Bereich (`kind: "personal"`) für private Daten. Der Server legt ihn beim ersten Abgleich des Kontos an, Geräte erfahren ihn nur über den Abgleich; bis zum ersten abgeschlossenen Abgleich nach der Anmeldung nimmt die App keine Kommandos an ([#124](https://github.com/Murmli/MurmliV2/issues/124)).

Ein geteilter Bereich, bei Einkaufslisten `kind: "shoppingList"`, entsteht nur mit dem `create` seines Wurzel-Datensatzes, bei Listen der Liste selbst. Seine ID ist die des Wurzel-Datensatzes, der Absender wird sein Besitzer. Jedes andere Kommando in einen Bereich, den es nicht gibt, ist `noAccess` und legt nichts an. Den Wurzel-Datensatz löscht nur der Besitzer, danach ist der Bereich mit allen Datensätzen und Mitgliedseinträgen für alle weg. Mitgliedschaft ändert kein Kommando, sie ändert sich nur online ([#129](https://github.com/Murmli/MurmliV2/issues/129)).

Sichtbar sind einem Konto sein persönlicher Bereich und jeder geteilte Bereich, in dem es einen aktiven Mitgliedseintrag hat und dessen Wurzel-Datentyp in `dataTypes` steht. Geteilte Bereiche, deren Wurzel-Datentyp nicht in `dataTypes` steht, schickt der Server nicht, nimmt sie nicht ins Token auf und meldet sie nicht als entfernt: Eine alte App sieht nie einen Bereich, den sie nicht darstellen kann, eine aktualisierte verwirft ihr Token und bekommt ihn mit dem vollständigen Stand.

Der Block eines geteilten Bereichs nennt in `members` immer alle Mitgliedseinträge, der persönliche Bereich hat keine. Ein Eintrag ist `{ memberId, name, role, state, isOwn }`, `isOwn` kennzeichnet den eigenen. Datensätze nennen Personen nur über die Mitglieds-ID, etwa in `changedBy`; Konto-IDs stehen in keiner Antwort. `name` ist leer bis zum Anzeigenamen aus [#146](https://github.com/Murmli/MurmliV2/issues/146). Bis zum Teilen ([#143](https://github.com/Murmli/MurmliV2/issues/143)) hat jeder geteilte Bereich nur den Eintrag seines Besitzers (`role: "owner"`, `state: "active"`).

Entschieden, noch nicht gebaut: geteilte Bereiche mit Wurzel-Datensatz und Besitzer, Rollen und Mitgliedseinträge in `members`. Entfällt, sobald Bereiche, Besitzer und Mitgliedseinträge aus [#128](https://github.com/Murmli/MurmliV2/issues/128) gebaut sind.

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

Kategorie- und Einheitsschlüssel in den Beispielen sind Platzhalter, bis der Katalog steht ([Einkaufsliste](#einkaufsliste)).

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
          "fields": { "name": "Hafermilch", "quantity": 1, "unit": "liter", "category": "dairy", "checked": false, "language": "de", "createdAt": 1795400000123, "changedBy": "6e1c0b8a-3f2d-4a5e-9b7c-8d9e0f1a2b3c", "changedAt": 1795400000600 }
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
- `alreadyProcessed` trägt immer das gespeicherte Ergebnis, auch wenn der Server eine Verarbeitung nach einem unklaren Commit wiederholt oder dieselbe Anfrage zweimal gleichzeitig ankommt. Ein spät erneut gesendetes Kommando, das schon verarbeitet ist, wird so nie `tooOld`.
- Fehlt eines der Felder `id`, `kind`, `scopeId`, `dataType`, `recordId` und `time` oder lässt es sich nicht lesen (IDs keine UUID v4, `time` keine ganze Zahl im erlaubten Wertebereich), speichert der Server das Kommando nicht; es bekommt bei jeder Wiederholung wieder `rejected` mit `invalid`.
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
- Untergrenze: Das Aufräumen entfernt Löschmarkierungen, die älter als 45 Tage sind, und hebt dabei die Untergrenze ihres Bereichs an. Ein Token darunter könnte entfernte Löschungen verpasst haben und bekommt den vollständigen Stand. Jede Revision, die `/v1/sync` bedient, beachtet die Untergrenze, so trifft ein Zurücknehmen des Aufräumens oder ein Rollback auf eine ältere Revision nie auf einen Leser ohne sie.

Entschieden, noch nicht gebaut: das Aufräumen, das abgelaufene Löschmarkierungen und Kommando-IDs entfernt und die Untergrenze anhebt; bis dahin bleibt sie 0. Entfällt, sobald das Aufräumen aus [#132](https://github.com/Murmli/MurmliV2/issues/132) gebaut ist.

#### Mitgelieferter Stand

Zu jedem Kommando, dessen Ergebnis weder `applied` noch das reservierte `accepted` ist, liefert der Server den aktuellen Stand seines Ziels mit, unabhängig vom Token. Das gilt auch für jedes `alreadyProcessed`, gleich mit welchem Original, und für `failed`. Ziel ist bei `alreadyProcessed` das gespeicherte des ersten Kommandos mit dieser ID, sonst das des Kommandos:

1. Sieht das Konto den Bereich nach der Verarbeitung nicht: der Bereich in `removedScopes`, nie ein Datensatz.
2. War das Kommando das `delete` eines Wurzel-Datensatzes: der vollständige Stand des Bereichs. Das Gerät hat den Bereich schon lokal entfernt, der Wurzel-Datensatz allein brächte die übrigen Datensätze nicht zurück.
3. Hat der Bereich in dieser Antwort schon den vollständigen Stand: nichts weiter.
4. Steht der Datentyp nicht in `dataTypes`: nichts.
5. Gibt es den Datensatz in diesem Bereich: der Datensatz im Block des Bereichs.
6. Sonst: `{ dataType, id }` in `deletedRecords` des Blocks, auch wenn die ID in einem fremden Bereich liegt, der nie genannt wird.

Der Server sucht nur im genannten Bereich, eine fremde Bereichs-ID gelangt so nie an das Gerät. Ein Kommando, das der Server nicht speichert, liefert sein Ziel mit, wenn sich `scopeId` und `recordId` als UUID lesen lassen (gleich welcher Version) und der Server den Datentyp kennt. Ohne diese Regel bliebe ein vorläufiger Wert dauerhaft auf dem Gerät stehen, wenn genau die Antwort mit der Ablehnung verloren geht.

#### Entfernte Bereiche

`removedScopes` nennt jeden Bereich, den das Token nennt oder auf den ein Kommando der Anfrage mit lesbarem Ziel zielte (bei `alreadyProcessed` das gespeicherte), und den das Konto nach der Verarbeitung nicht sieht. So verschwindet auch eine Liste, die ein anderes Gerät desselben Kontos gelöscht hat, bevor dieses Gerät sie je im Token hatte. `removedByOwnAccount` ist `true`, wenn ein Gerät des Kontos den Wurzel-Datensatz des Bereichs gelöscht hat; Verlassen kommt mit [#129](https://github.com/Murmli/MurmliV2/issues/129) dazu. Das Gerät löscht den Bereich mit Datensätzen, Mitgliedern und wartenden Kommandos und legt einen Hinweis ab, außer bei `removedByOwnAccount: true`.

Entschieden, noch nicht gebaut: entfernte Bereiche mit `removedByOwnAccount`. Entfällt, sobald Bereiche, Besitzer und Mitgliedseinträge aus [#128](https://github.com/Murmli/MurmliV2/issues/128) gebaut sind.

### Kompression

Der Server komprimiert eine Antwort genau dann mit gzip, wenn mindestens ein Block `fullState: true` hat und `Accept-Encoding` `gzip` mit einem q über 0 nennt. Dann trägt sie genau `Content-Encoding: gzip`, ein einziger Wert in Kleinbuchstaben, denn dart:io entpackt nur bei genau diesem Wert. Jede Antwort mit Status 200 trägt `Vary: Accept-Encoding`. Änderungen ohne vollständigen Stand bleiben unkomprimiert ([Entscheidungen](#entscheidungen)).

Entschieden, noch nicht gebaut: gzip für vollständige Stände. Entfällt, sobald die Kompression vollständiger Stände aus [#123](https://github.com/Murmli/MurmliV2/issues/123) gebaut ist.

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

Nach Anmeldung und Abruf des Kontos prüft der Server in dieser Reihenfolge: Konto (410), fehlender `Content-Type` (415), Geräte-ID (400), Anfragen je Gerät und Konto (429), Größe des Bodys (413), JSON und Rahmen (400), Zahl der Kommandos (413), Kommandos je Konto (429). Die ersten vier Prüfungen laufen, bevor er den Body liest. Einen vorhandenen `Content-Type`, der kein JSON ist, beantwortet schon das Routing mit 415, vor allen diesen Prüfungen.

Ein Gerät entfernt ein Kommando nur wegen eines Ergebnisses in einer lesbaren Antwort oder weil es das Kommando nach den Regeln aus [#124](https://github.com/Murmli/MurmliV2/issues/124) aussortiert, nie wegen eines Statuscodes: Schon eine Zwischenstation, die mit 400 antwortet, kostete sonst Änderungen.

```json
{ "type": "tag:murmli.de,2026:service-unavailable", "title": "Service Unavailable", "status": 503, "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" }
```

Den Typ `service-unavailable` mit `Retry-After` liefert schon heute jeder Endpunkt, wenn der Abruf des Kontos zu Beginn jeder Anfrage an einer Störung der Datenbank scheitert ([api.md](api.md#problem-typen)).

Entschieden, noch nicht gebaut: 410 für ein Konto, dessen Löschung begonnen hat. Entfällt, sobald die Kontolöschung mit abgeglichenen Daten aus [#130](https://github.com/Murmli/MurmliV2/issues/130) gebaut ist.

Entschieden, noch nicht gebaut: 429 `rate-limited`. Entfällt, sobald das Rate-Limit aus [#135](https://github.com/Murmli/MurmliV2/issues/135) gebaut ist.

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

Entschieden, noch nicht gebaut: die Regeln dieses Kapitels, soweit kein eigener Vermerk ein anderes Issue nennt. Entfällt, sobald der Sync-Kern aus [#123](https://github.com/Murmli/MurmliV2/issues/123) gebaut ist.

- **Je Feld gewinnt die spätere Zeit.** Der Server merkt sich je Feldgruppe den Zeitpunkt der letzten übernommenen Änderung und übernimmt eine Gruppe, wenn `time` mindestens so groß ist. Bei gleicher Zeit gewinnt also das später verarbeitete Kommando, so zählt bei zwei schnellen Aktionen desselben Geräts die zweite. Gruppen gewinnen einzeln: Ändert ein Gerät die Menge und ein anderes die Kategorie, bleiben beide. Ist mindestens eine Gruppe übernommen, ist das Ergebnis `applied` und diese Gruppen tragen danach `time`; sonst `ignored` mit `lostToNewer`.
- **Löschen gewinnt immer**, ohne Blick auf die Zeit. Ein gelöschter Datensatz kommt nicht wieder, auch nicht mit derselben ID: Jedes spätere Kommando an ihn in seinem Bereich ist `ignored` mit `targetDeleted`.
- **`create` auf eine vorhandene ID** im selben Bereich wirkt wie `setFields` mit allen gesendeten Gruppen; Felder, die nur beim Anlegen gelten, und die Angaben des Servers zum Anlegen (etwa `createdAt`) bleiben. Liegt die ID lebend oder gelöscht in einem anderen Bereich: `noAccess`.
- **`setFields` und `delete` ohne Ziel:** Gibt es den Datensatz nicht oder nicht mehr, ist das Kommando `ignored` mit `targetDeleted`; es legt nie einen Datensatz an. Darauf beruht, dass alte Löschmarkierungen aufgeräumt werden dürfen.
- **Regeln eines Datentyps** können ein Kommando zusätzlich ignorieren (`lostToNewer`) oder ablehnen (vorhandener Grund).
- **Zeitpunkt:** Liegt `time` mehr als 5 Minuten nach der Uhr des Servers, lehnt er das Kommando als `clockAhead` ab; es verfällt mit Hinweis, statt zu warten. Liegt `time` mehr als 45 Tage davor, lehnt er es als `tooOld` ab: Wiederholungen erkennt er nur an gespeicherten Kommando-IDs, und eine ID räumt er erst auf, wenn ein erneut gesendetes Kommando ohnehin `tooOld` wäre; ohne die Ablehnung legte ein spät erneut gesendetes `create` einen längst gelöschten Datensatz wieder an. Ist die ID noch gespeichert, gewinnt `alreadyProcessed`.
- **Der Server als Schreiber** nutzt dieselben Regeln mit seiner eigenen Uhr als `time`: Wiederholbarkeit, Werte und Grenzen, Regeln des Datentyps, Konfliktregeln, Löschen gewinnt, keine Wiederkehr, Versionen und Löschmarkierungen. Er schreibt im Namen eines Kontos, etwa beim Anlegen der ersten Liste, oder ohne Person, etwa beim Entfernen abgehakter Artikel, und darf Felder des Servers setzen. Prüfungen der Uhr, der Mitgliedschaft und der Rolle, Rate-Limits und später die Prüfung „Konto darf nicht schreiben ([#133](https://github.com/Murmli/MurmliV2/issues/133))“ entfallen, und eine Bedingung kann eine Änderung verwerfen (Schritt 14). Geräte erfahren seine Änderungen wie jede andere.

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
| 14 | Bedingung einer Änderung des Servers nicht erfüllt | `ignored` mit `lostToNewer` |
| 15 | Regeln des Datentyps | `ignored` oder `rejected` |
| 16 | Felder des Servers | gesetzt, etwa `changedBy` und `changedAt` |

Entschieden, noch nicht gebaut: geteilte Bereiche mit Wurzel-Datensatz, Besitzer und Rollen (Schritte 2 bis 4 und 8, Zugriff über Mitgliedseinträge). Entfällt, sobald Bereiche, Besitzer und Mitgliedseinträge aus [#128](https://github.com/Murmli/MurmliV2/issues/128) gebaut sind.

Entschieden, noch nicht gebaut: der Abbruch mit 410, während das Konto gelöscht wird (Schritt 2). Entfällt, sobald die Kontolöschung mit abgeglichenen Daten aus [#130](https://github.com/Murmli/MurmliV2/issues/130) gebaut ist.

Entschieden, noch nicht gebaut: der Server als Schreiber (Schritt 14). Entfällt, sobald er mit der Einkaufsliste aus [#141](https://github.com/Murmli/MurmliV2/issues/141) gebaut ist.

## Einkaufsliste

Erster Datentyp ([#141](https://github.com/Murmli/MurmliV2/issues/141)). Jede Liste ist ein geteilter Bereich der Art `shoppingList`, ihr Wurzel-Datensatz ist die Liste selbst (`shoppingList`, ID gleich Bereichs-ID), ihre Artikel sind Datensätze vom Typ `shoppingListItem` im selben Bereich. In diesem Kapitel verweisen Zahlen in Klammern wie in den Issues auf Fragen im internen Fragenkatalog.

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
| `language` | nur bei `create`, Pflicht | 2 bis 3 Kleinbuchstaben (`^[a-z]{2,3}$`) |
| `createdAt` | Feld des Servers | `time` des `create`, beim Server als Schreiber also seine Uhr |
| `changedAt` | Feld des Servers | `time` der letzten übernommenen Änderung, auch beim Anlegen, Abhaken und Herausnehmen des Hakens |
| `changedBy` | Feld des Servers | Mitglieds-ID der Person, die zuletzt geändert hat („zuletzt geändert von“), auch beim Anlegen, Abhaken und Herausnehmen des Hakens; `null`, wenn der Server den Artikel ohne Person angelegt hat |

- Als Angabe zur Person zeigen Geräte an einem Artikel nur „zuletzt geändert von“ (`changedBy`). Wer ihn angelegt hat, hält der Server zusätzlich intern fest, für eine Datenauskunft und das Entfernen der Daten einer Person ([#131](https://github.com/Murmli/MurmliV2/issues/131)), und schickt es nie an Geräte.
- `changedAt` und `changedBy` beschreiben immer dieselbe Änderung: Ändert der Server einen Artikel ohne Person, bleiben beide stehen.
- Abgehakte Artikel bleiben änderbar, die Einkaufsliste hat keine eigenen Regeln, die Kommandos ignorieren oder ablehnen.
- Kategorien und Einheiten sind feste Schlüssel aus `src/sync-cases/shopping/catalog.json` (`{ "categories": [...], "units": [...] }`), dieselben IDs wie die Kategorien und Einheiten aus [#80](https://github.com/Murmli/MurmliV2/issues/80). Eine neue Kategorie ist ein zusätzlicher Schlüssel.
- Erste Liste: Mit dem persönlichen Bereich legt der Server beim ersten Abgleich eines Kontos genau eine Liste an, auch wenn sich zwei Geräte gleichzeitig zum ersten Mal anmelden. Ihr Name ist leer, die App zeigt dafür einen übersetzten Standardnamen. Wer sie löscht, hat danach keine Liste, der Server legt keine neue an.
- Abgehakte Artikel entfernt der Server beim Aufräumen, sobald das Abhaken mehr als 30 Tage zurückliegt (`ShoppingLists:CheckedItemRetention`); maßgeblich ist die Ankunft des Abhakens am Server, nicht die Uhr des Geräts. Nimmt jemand den Haken vorher heraus, bleibt der Artikel. Geräte erfahren das wie jede Löschung.
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

Entschieden, noch nicht gebaut: die Einkaufsliste am Server mit Katalog, erster Liste, dem Entfernen abgehakter Artikel und den Fällen unter `src/sync-cases/shopping/`. Entfällt, sobald die Einkaufsliste aus [#141](https://github.com/Murmli/MurmliV2/issues/141) gebaut ist.

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

- Die Werte sind Standards im Code, Umgebungen können sie überschreiben (`Sync:MaxRequestBytes`, `Sync:MaxCommandsPerRequest`, `Sync:CommandPhaseBudget`, `Sync:RateLimits:*`, `Sync:DataTypes:<name>:MaxRecordsPerScope`, `Sync:DataTypes:<name>:MaxOwnedScopesPerAccount`); Längen von Feldern stehen nur im Code. Die App prüft dieselben Grenzen der Datentypen vor dem Einreihen, so entsteht kein Kommando, das der Server nie annimmt ([#124](https://github.com/Murmli/MurmliV2/issues/124)).
- Die Grenze je Gerät gilt je Konto und Gerät, eine geratene Geräte-ID bremst kein anderes Konto. Erneut gesendete, schon verarbeitete Kommandos zählen mit. Die Zähler gelten je Instanz der API.
- Bei Kommandos je Konto kann `Retry-After` bis zu einer Minute zu kurz sein: Ein Gerät, das genau wartet, bekommt dann noch einmal 429 mit `Retry-After` von einer Minute, behandelt sie wie die erste und verliert nichts.

Entschieden, noch nicht gebaut: Größe des Bodys, Kommandos je Anfrage, Feldwerte, Datensätze je Bereich, Löschungen in einem Delta und Zeit für Kommandos. Entfällt, sobald der Sync-Kern aus [#123](https://github.com/Murmli/MurmliV2/issues/123) gebaut ist.

Entschieden, noch nicht gebaut: eigene Bereiche je Konto. Entfällt, sobald Bereiche, Besitzer und Mitgliedseinträge aus [#128](https://github.com/Murmli/MurmliV2/issues/128) gebaut sind.

Entschieden, noch nicht gebaut: die Werte der Einkaufsliste (Name eines Artikels, Artikel je Liste, eigene Listen). Entfällt, sobald die Einkaufsliste aus [#141](https://github.com/Murmli/MurmliV2/issues/141) gebaut ist.

Entschieden, noch nicht gebaut: Anfragen je Gerät und je Konto, Kommandos je Konto. Entfällt, sobald das Rate-Limit aus [#135](https://github.com/Murmli/MurmliV2/issues/135) gebaut ist.

## Fallsammlung

Die Konfliktregeln liegen zusätzlich als gemeinsame Fallsammlung vor: Ausgangsstand, zwei Kommandos in beiden Reihenfolgen, erwartete Ergebnisse und erwarteter Stand. Die Tests des Backends und die Simulation der App ([#136](https://github.com/Murmli/MurmliV2/issues/136)) lesen dieselben Dateien. Je Fall eine JSON-Datei unter `src/sync-cases/` (JSON, damit keine Seite ein Paket braucht): `core/` mit den künstlichen Datentypen der Tests (`testNote` im persönlichen Bereich, `testBoard` als Wurzel mit `testCard` darin), `shopping/` mit der Einkaufsliste. Die Simulation liest sie aus `../sync-cases`, von `src/app` aus.

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

Entschieden, noch nicht gebaut: `src/sync-cases/` mit den Fällen des Kerns und ihr Leser in den Tests des Backends. Entfällt, sobald die ersten Fälle mit dem Sync-Kern aus [#123](https://github.com/Murmli/MurmliV2/issues/123) im Repo liegen.

Entschieden, noch nicht gebaut: der Leser in der Simulation der App. Entfällt, sobald die Simulation aus [#136](https://github.com/Murmli/MurmliV2/issues/136) gebaut ist.

## Entscheidungen

| Entscheidung | Grund |
| --- | --- |
| Zeitpunkte als ganze Unix-Millisekunden in UTC, Dauern als ganze Sekunden | Keine Zeitzonen beim Lesen, exakte Gleichheit. Verworfen: ISO-8601-Strings (Werte ohne Offset werden als Ortszeit gelesen, unterschiedliche Genauigkeit). |
| Geräte-ID im Header `Murmli-Device-Id` | Die Grenze je Gerät greift, bevor der Body gelesen wird, und das `GET` des Live-Kanals ([#125](https://github.com/Murmli/MurmliV2/issues/125)) nutzt denselben Header. Verworfen: Feld im Body. |
| Rahmen der Anfrage tolerant, Kommandos streng | Ein Rollback der API darf neuere Apps nicht abweisen, deshalb ignoriert der Server unbekannte Felder im Rahmen. Unbekannte Felder eines Kommandos machen nur dieses ungültig, wie [#122](https://github.com/Murmli/MurmliV2/issues/122) es verlangt. Verworfen: überall ignorieren (widerspricht [#122](https://github.com/Murmli/MurmliV2/issues/122)), überall streng samt strengem Lesen des ganzen Bodys (ein einziges fehlerhaftes Kommando würde zur 400 der ganzen Anfrage, die die App endlos wiederholt). |
| Problemtypen als Tag-URI `tag:murmli.de,2026:<code>` in `type` | `type` ist nach RFC 9457 die eigentliche Kennung, Tag-URIs sind für Kennungen gedacht, die sich nicht abrufen lassen. Verworfen: nur der Statuscode (Zwischenstationen und das Framework liefern dieselben), eine Erweiterung `code`, `urn:`- oder `https:`-URIs. |
| Token binär mit fester Breite, base64url | Einfach zu prüfen, deterministisch. Verworfen: JSON-Map (größer, keine feste Reihenfolge), Versionen als LEB128 (etwas kleiner, mehr Parsen). |
| gzip nur für vollständige Stände, gesetzt von der Antwort selbst | Unabhängig von der Umgebung, ohne Wirkung auf den Live-Kanal. Deltas bleiben unkomprimiert, denn BREACH braucht vom Angreifer gesteuerten Text und viele beobachtbare Antworten desselben Opfers; vollständige Stände der Bereiche eines anderen Kontos kann ein Mitglied nicht beliebig auslösen. Verworfen: die Middleware zur Antwortkompression von ASP.NET Core (ihre HTTPS-Prüfung kippt zwischen lokalem HTTP und den weitergereichten Headern in Azure, sie träfe den Ereignisstrom des Live-Kanals und bevorzugt zstd und br). |
| Kein Header für die App-Version, nur der Problemtyp `app-version-unsupported` reserviert | Wie die App ihre Version nennt und welche Versionen gelten, entscheidet [#69](https://github.com/Murmli/MurmliV2/issues/69). Verworfen: Header und Mindestversionen schon jetzt. |
| `settings.maxRequestBytes` bindet das Gerät, die 413 ist nur ein Sicherheitsnetz | Eine 413 mit `Connection: close` kann bei dio als abgebrochene Verbindung ankommen. Verworfen: übergroße Bodys am Server bis zu einer zweiten, harten Grenze zu Ende lesen (eine Grenze mehr, darüber scheitert es trotzdem). |
