<!-- claudete:general-conventions:start -->
# Allgemeine Konventionen

Stack-unabhängige Regeln für dieses Projekt. Definiert das Repo an anderer
Stelle (CLAUDE.md bzw. AGENTS.md, spezifischere Skills) eine abweichende
Konvention, geht die vor.

## Arbeitsweise

- Alignment vor Aktion: Bei nicht-trivialen Aufgaben vor der Umsetzung in
  wenigen Sätzen (1) Verständnis, (2) geplanten Ansatz und (3) Trade-offs
  bzw. Risiken zusammenfassen und die Bestätigung abwarten. Bei Aufgaben über
  3+ Dateien oder mit Architektur-Entscheidungen in den Plan-Modus gehen.
  Ausnahme: Verlangt der Auftrag ausdrücklich autonome Umsetzung oder läuft
  die Session unbeaufsichtigt, Annahmen explizit nennen und weiterarbeiten
  statt zu blockieren.
- Schlägt der User nach einer Implementierung einen anderen Ansatz vor, alt
  und neu mit konkreten Pros und Cons vergleichen, bevor gewechselt wird –
  erhebliche Nachteile klar benennen, damit die Wahl informiert ist.
- Läuft ein Ansatz gegen die Wand (wiederholte Fehlschläge, widerlegte
  Annahmen), anhalten und neu planen statt weiterzudrücken.
- Codebase-Exploration und Recherche an Subagents auslagern – der
  Haupt-Kontext hält Entscheidungen und Ergebnisse, nicht den Suchprozess.
- Jede Programmierarbeit läuft über einen Subagent, nie im Haupt-Kontext –
  auch bei kleinen Änderungen. Der Haupt-Kontext recherchiert, entscheidet
  und schneidet die Aufträge zu; mehrstufige Arbeit wird in eigene
  Subagent-Schritte zerlegt (Implementierung, Tests, Review-Fixes).
- Der Auftrag an einen solchen Subagent ist strikt und abschließend: die zu
  ändernden Dateien mit Pfaden, das gewünschte Zielverhalten, die Ergebnisse
  der Vorab-Recherche (API-Signaturen, bestehende Muster im Repo,
  Doku-Auszüge), die geltenden Konventionen sowie die Akzeptanzkriterien
  samt der Befehle, die sie prüfen. Was nicht im Auftrag steht, wird nicht
  angefasst.
- Diese Subagents forschen nicht mehr – Exploration und Recherche sind
  vorher passiert und stecken im Auftrag. Fehlt etwas oder widerspricht die
  Vorgabe dem Code, meldet der Subagent das zurück, statt selbst zu suchen,
  zu entscheiden oder zu raten.
- Vor Änderungen an nebenläufigem Code identifizieren: geteilter
  veränderlicher Zustand, Ordering-Garantien beim Verschränken von
  Operationen, bestehende Synchronisationsgrenzen; bei async-Code zusätzlich
  Cancellation-Propagation, Backpressure und Atomicity.
- Selbst-Review vor der Fertigmeldung: geänderte Dateien erneut lesen (nicht
  aus dem Gedächtnis), auf unbenutzte Variablen, fehlende Null-Checks,
  inkonsistente Benennung und unbehandelte Edge-Cases prüfen; bauen und die
  betroffenen Tests ausführen.
- Simplicity-Check vor der Fertigmeldung: jede neue Variable, jeder Wrapper,
  jede Zwischen-Collection und jeder Parameter muss sein Gewicht tragen –
  einmal sofort benutzte Werte inlinen; wirkt etwas over-engineered, zuerst
  vereinfachen.

## Antworten im Chat

- Auflistungen im Chat immer nummerieren (`1.`, `2.`, …), nie als
  Bullet-Liste – damit sich der User per Nummer auf einzelne Punkte beziehen
  kann. Das gilt besonders für Rückfragen an den User: jede Frage bekommt
  eine eigene Nummer, auch wenn es nur zwei sind.
- Die Nummerierung läuft über die ganze Antwort durch und beginnt nicht pro
  Absatz oder Liste neu – jede Nummer ist innerhalb einer Antwort eindeutig.
  Folgt auf eine Liste mit `1.` bis `3.` eine weitere, beginnt diese bei `4.`
  (Markdown übernimmt die Startnummer des ersten Eintrags).

## Code-Stil

- Code, Bezeichner und Code-Kommentare sind immer auf Englisch – unabhängig
  von der Sprache im Chat.
- Standard ist null Kommentare. Ein Kommentar wird nur geschrieben, wenn er
  etwas nennt, das der Leser dem Code nicht ansehen kann: einen nicht
  offensichtlichen Algorithmus, einen behandelten Edge-Case, einen Workaround
  oder eine bewusste Entscheidung gegen das Naheliegende – im Zweifel
  weglassen. Dann nur die Essenz, so kurz wie möglich, z.B.
  `// Safari fires pagehide twice; the guard drops the second call`.
- Wirkt Code ohne Kommentar unklar, zuerst Namen verbessern oder eine
  Funktion extrahieren – ein Kommentar ist nur erlaubt, wenn beides die
  Information nicht tragen kann.
- Verbotene Kommentar-Muster – niemals schreiben, auch nicht in Varianten;
  die Liste ist beispielhaft, nicht abschließend:
  - Paraphrase von Code oder Namen: kein `// enables the cooking mode` an
    `isCookingModeEnabled`, kein `// save the user` über
    `repository.save(user)`.
  - Schritt- und Abschnitts-Erzählung: kein `// Validate input`, `// Setup`,
    `// Main logic`, `// --- Helpers ---`; in Tests kein `// Arrange` /
    `// Act` / `// Assert`.
  - Änderungs-Erzählung: kein `// now uses the new API`,
    `// changed to async`, `// as requested` – die Änderung erzählen Diff
    und Commit-Message, nicht der Code.
  - Signatur-Echo: kein JSDoc/XML-Doc/Docstring, der nur Name, Parameter und
    Rückgabetyp umformuliert. Doc-Comments nur, wo das Projekt oder die
    Aufgabe sie verlangt – und dann ohne Parameter-/Return-Echos, nur mit
    Inhalt jenseits der Signatur (Einheiten, Fehlerverhalten,
    Nebenwirkungen).
- Maschinen-Direktiven (`// eslint-disable-next-line`, `# type: ignore`,
  Pragmas, Lizenz-Header) zählen nicht als Kommentare im Sinne dieser Regel.
- Die Regel gilt für selbst geschriebene Kommentare – bestehende Kommentare
  in fremdem Code bleiben unangetastet.
- Keine Abkürzungen in Bezeichnern – Worte immer ausschreiben: `index` statt
  `i` in Schleifen, `template` statt `tpl`. Ausgeschriebene Namen sind besser
  lesbar.
- Jeder Code ist Produktionscode: sauber, vollständig und wartbar – keine
  Provisorien, keine auskommentierten Reste, keine "TODO später"-Lösungen.
  Ausnahme nur, wenn explizit Prototyp- oder Wegwerfcode angefordert wird.
- Generierte Artefakte nie von Hand editieren – Generator, Template oder
  Quelle ändern und neu generieren. Sind generierte Dateien committet, das
  regenerierte Ergebnis zusammen mit der auslösenden Änderung committen.
  Generator-Fehlschläge sichtbar machen (Fehler-Artefakt oder harter
  Abbruch), nie still verschlucken.
- In zeitabhängiger Logik, deren Verhalten Tests kontrollieren müssen, die
  Systemzeit nicht direkt lesen – eine testbar kontrollierbare Uhr verwenden
  (die injizierte Abstraktion, wo der Stack eine bietet).
- Nutzt das Projekt strukturiertes Logging, loggt dasselbe Konzept immer
  unter demselben Property-Namen, damit Logs zuverlässig abfragbar bleiben.

## Testing

- Tests prüfen ausschließlich eigenes Anwendungsverhalten: Geschäftslogik,
  Edge-Cases, Fehlerpfade. Niemals das zugrunde liegende Framework testen –
  z.B. kein Test, ob eine Variable sauber ans UI gebunden wird, ob ein Getter
  den gesetzten Wert liefert oder ob ein Framework-Feature funktioniert; das
  deckt das Framework selbst ab.
- Leitfrage vor jedem Test: Welches Verhalten der Anwendung bricht, wenn
  dieser Test rot wird? Gibt es keine konkrete Antwort, den Test nicht
  schreiben.
- Kein Test-only-Code in Produktion: keine Member, Konstruktoren oder
  Factories, deren einziger Aufrufer ein Test ist (kein `CreateForTesting`,
  keine Seed-/Reset-Methoden nur für Test-Setups) – solche Helfer gehören
  in den Test-Code (Testprojekt bzw. Test-Verzeichnis).
- Sichtbarkeit nie für Tests erweitern: ein Member wird nicht `public` oder
  `internal` (bzw. das Äquivalent des Stacks), nur um testbar zu sein – über
  die bestehende öffentliche Oberfläche testen oder die Logik in einen
  eigenen Typ extrahieren, dessen Sichtbarkeit produktiv begründet ist.
- Vor jedem Mock oder Fake die echte Implementierung ansehen: den echten Typ
  verwenden, wenn er billig zu konstruieren ist (keine I/O, kein globaler
  Zustand, kein DI-Graph) oder nennenswerte Logik trägt – ein abweichender
  Stub maskiert Bugs oder erfindet Fehler, die produktiv nie auftreten.
  Mocken nur, wenn der echte Typ schwere Abhängigkeiten zieht (Datenbank,
  Netzwerk, externe Services); im Zweifel den User fragen statt einen Stub
  zu erfinden.
- "Keine Seiteneffekte"-Assertions dürfen nicht konstruktionsbedingt
  tautologisch wahr sein: Kann der Input-Typ die Daten für den Seiteneffekt
  strukturell gar nicht tragen, prüft der Test nichts – weglassen, wenn ein
  echter Kontrast-Test (gemischter Erfolgs- und Fehlerfall) existiert.
- Organisation: zuerst nach einer bestehenden Testdatei bzw. -Suite für
  denselben Member bzw. dasselbe Feature suchen und dort ergänzen; eine neue
  Datei nur bei echtem neuen Schnitt. Gemeinsame Setup-Infrastruktur
  (Basisklasse, Fixture, geteilter Hook) erst, wenn 2+ Stellen Setup
  duplizieren – nie auf Vorrat.
- Gemeinsames Setup in die Setup-Mechanismen des Frameworks (Konstruktor,
  Setup-Hooks, Fixtures, Helper) – der Arrange-Teil eines Tests enthält nur
  szenariospezifische Werte.
- Pro Repo genau eine Assertion- und genau eine Mocking-Bibliothek.
  Legacy-Muster (alte Assertion-Lib, alter Namensstil) bekommen keine neuen
  Verwendungen – auch beim Ergänzen in Legacy-Dateien den kanonischen Stil
  verwenden.
- Testnamen nennen getestetes Verhalten, Szenario und erwartetes Ergebnis
  (z.B. `AddRow_EmptyTable_AddsRow` – sinngemäß je Test-Framework).

## Branches & Commits

- Branch-Namen beim Anlegen: nur die Prefixe `feature/`, `fix/` und `release/`,
  immer ausgeschrieben (`feature/abc`, nicht `feat/abc`); andere Prefixe nur
  auf explizite Anweisung.
- Gehört der Branch zu einem GitHub-Issue, steht dessen Nummer direkt nach
  dem Prefix vor dem beschreibenden Namen: `feature/123-add-retry-logic`.
  Nummern nie raten – nur verwenden, wenn das Issue in der Aufgabe genannt
  oder vorher nachgeschlagen wurde.
- Änderungen automatisch committen, ohne nachzufragen – in sinnvollen,
  logischen Blöcken: Zusammengehöriges in einem Commit, Unzusammenhängendes
  getrennt.
- Commit-Messages bestehen ausschließlich aus dem Titel (eine Zeile) und sind
  immer auf Englisch – einzige Ausnahme ist die Issue-Referenz unten.
- Commit-Messages folgen Conventional Commits – außer das Repo beschreibt eine
  eigene Konvention, dann gilt die. Standard ist `type: beschreibung`; einen
  Scope (`type(scope): beschreibung`) nur verwenden, wenn das Repo Scopes
  definiert.
- Erlaubte Typen – genau diese, keine anderen:
  - `build`: Änderungen am Build-System oder an externen Dependencies
  - `ci`: Änderungen an CI-Konfiguration und -Skripten
  - `docs`: reine Dokumentations-Änderungen
  - `feat`: ein neues Feature
  - `fix`: ein Bugfix
  - `perf`: eine Code-Änderung, die die Performance verbessert
  - `refactor`: eine Code-Änderung, die weder einen Bug behebt noch ein
    Feature hinzufügt
  - `style`: Änderungen ohne Einfluss auf die Bedeutung des Codes
    (Whitespace, Formatierung, fehlende Semikolons, …)
  - `test`: fehlende Tests ergänzen oder bestehende Tests korrigieren
- Niemals einen Commit-Body schreiben – auch keine Footer/Trailer wie
  `Co-Authored-By` oder "Generated with"-Zeilen.
- Ausnahme: Gehört ein Commit zu einem GitHub-Issue, besteht der Body aus genau
  einer Zeile `Refs #123`; mehrere Issues bekommen je eine eigene Zeile. Kein
  Closing-Keyword im Commit – das gehört in die PR-Beschreibung (siehe unten).
  Nummern nie raten: nur referenzieren, wenn das Issue in der Aufgabe genannt
  oder vorher nachgeschlagen wurde.

## GitHub-Issues

- Issues beschreiben Problem bzw. Anforderung rein fachlich: was, für wen,
  warum, erwartetes Verhalten, Akzeptanzkriterien. Kein Lösungsweg und keine
  Implementierungsskizze – außer der Ansatz wurde vorher explizit gemeinsam
  erarbeitet, dann kommt genau dieser abgestimmte Stand hinein.
- Keine Referenzen auf Dateien, Klassen oder andere Code-Stellen: Issues
  entstehen oft lange vor der Umsetzung, der Code bewegt sich weiter und die
  Referenzen veralten. Fachliche Begriffe statt Code-Symbole verwenden.
- Fließtext ohne harte Zeilenumbrüche schreiben – ein Absatz ist eine Zeile,
  GitHub bricht beim Rendern selbst um. Zeilenumbrüche nur, wo Markdown sie
  braucht (Absatzwechsel, Listen, Code-Blöcke).
- Vor dem Anlegen die fachliche Konzeption im Dialog schärfen: Unklarheiten,
  Edge-Cases und offene Entscheidungen aktiv ansprechen und klären – ein
  Issue wird erst angelegt, wenn keine Fragen offen sind.
- Hat ein Eltern-Issue Kinder (Epic mit Teilaufgaben), die Kinder als
  GitHub-Sub-Issues verknüpfen – nicht als Markdown-Liste oder Task-Liste
  mit `#123`-Links im Body: `gh issue create --parent <eltern-nummer>` beim
  Anlegen bzw. `gh issue edit <eltern-nummer> --add-sub-issue <nummer>`
  nachträglich.

## Pull Requests

- PR-Titel sind immer auf Englisch und folgen nicht Conventional Commits –
  kein `feat:`/`fix:`-Prefix, sondern ein normaler beschreibender Titel
  (z.B. "Add retry logic to the sync job" statt "feat: add retry logic to
  the sync job").
- Für PR-Beschreibungen gilt wie bei Issues: Fließtext ohne harte
  Zeilenumbrüche – GitHub bricht beim Rendern selbst um.
- Gehört ein PR zu einem GitHub-Issue, steht das Closing-Keyword in der
  PR-Beschreibung – `Fixes #123` bei Bugs, sonst `Closes #123`; mehrere
  Issues bekommen je eine eigene Zeile.
- Standard ist eine leere PR-Beschreibung. Hinein kommen nur das
  Closing-Keyword (oben) und, soweit Titel, verlinktes Issue und Diff es
  nicht schon sagen, ein bis wenige Sätze zu Was und Warum sowie
  Reviewer-Wissen, das dem Diff nicht anzusehen ist: Breaking Changes,
  Migrations- oder Deploy-Schritte, manuell zu prüfendes Verhalten, bewusste
  Entscheidungen gegen das Naheliegende. Leitfrage vor jedem Satz: Sagt es
  Titel, Issue oder Diff schon? Wenn ja, weglassen – auch wenn die
  Beschreibung dann nur aus dem Closing-Keyword besteht oder leer bleibt.
- Verbotene Muster in PR-Beschreibungen – niemals schreiben, auch nicht in
  Varianten; die Liste ist beispielhaft, nicht abschließend:
  Boilerplate-Überschriften (`## Summary`, `## Changes`, `## Test plan`),
  Nacherzählung der Änderungen als Bullets oder Fließtext, Listen geänderter
  Dateien, Wiederholung von PR-Titel oder Issue-Text, Protokoll des eigenen
  Vorgehens oder Testens, Checklisten, Emojis, "Generated with"-Footer.
- Hängen die referenzierten Issues an einem Milestone, den PR demselben
  Milestone zuordnen (`gh pr edit <nummer> --milestone <titel>`). GitHub
  erlaubt nur einen Milestone pro PR – verteilen sich die Issues auf
  mehrere, nachfragen statt raten.
- Kommen nach dem Erstellen eines PRs weitere Anmerkungen, die thematisch zu
  diesem PR gehören, zuerst prüfen, ob der PR schon im Review ist:
  `gh pr view <nummer> --json reviewRequests,reviews`. Sind beide leer, den
  bestehenden PR aktualisieren (auf denselben Branch pushen) statt einen neuen
  zu eröffnen. Ist ein Reviewer assigned oder ein Review abgegeben, den PR
  nicht mehr anfassen, sondern die Änderung als neuen PR machen.

## Dependencies & Versionen

- Beim Hinzufügen oder Aktualisieren von Dependencies (npm, NuGet, pip, …)
  niemals Versionen aus dem Trainingswissen übernehmen – immer zuerst die
  aktuell neueste Version ermitteln (z.B. `npm view <paket> version`,
  `dotnet package search`, PyPI-/Registry-Abfrage) und diese verwenden.
- Gleiches gilt für GitHub Actions (`uses:`-Referenzen), Basis-Images in
  Dockerfiles und Tool-Versionen in CI-Konfigurationen: vor dem Schreiben die
  neueste Major-Version bzw. das neueste Release nachschlagen (z.B. via
  `gh api repos/<owner>/<repo>/releases/latest`), nicht raten.
- Und für API-Oberflächen: bei Unsicherheit über Signaturen, Parameter oder
  Framework-Verhalten nie raten – aktuelle Doku nachschlagen und bestehende
  Verwendungen im Repo lesen.

## Agent-Dokumentation

Regeln für CLAUDE.md und weitere Agent-Instruktionsdateien im Repo.

- Die Instruktionen sind Teil des Deliverables: Ändert eine Aufgabe
  Architektur, Konventionen, Datenstrukturen oder Verhalten, das dort
  beschrieben ist, den betroffenen Abschnitt im selben Arbeitsgang
  aktualisieren – Doku und Code driften nie auseinander.
- Die Doku beschreibt den Ist-Zustand des Codes, nie das Soll. Entschiedenes,
  aber noch nicht Gebautes explizit als solches markieren – samt der
  Bedingung, wann die Notiz entfällt.
- Kuratierte Landkarte, kein Code-Spiegel: Ein nur im Code sichtbarer Fakt
  kommt nur hinein, wenn mehrere Kriterien zutreffen – wiederkehrend
  gebraucht, querschnittlich oder tragend (Contract/Invariante), nicht
  offensichtlich (Footgun), stabil, teuer herzuleiten. Lokale, leicht
  auffindbare Mechanik bleibt im Code; nie Signaturen auf Vorrat kopieren.
- Netto-Disziplin: Jede Ergänzung hat einen benannten Zielort. Zuerst
  prüfen, ob ein bestehender Eintrag das Wissen schon trägt, und dort
  schärfen statt daneben zu ergänzen; Redundantes oder Veraltetes im selben
  Zug entfernen.
- Nach einer User-Korrektur an einem projektweiten Muster die betroffene
  Stelle als konkrete Regel aktualisieren – nicht als vage Lektion irgendwo
  anhängen.
- Entscheidungen dort dokumentieren, wo ein künftiger Leser sie erneut
  vorschlagen würde: verworfene Alternativen mit Grund, bewusste
  Abweichungen vom Naheliegenden als beabsichtigt markiert, widerlegte
  (Optimierungs-)Hypothesen mit Datum, Messung und dem Vermerk, sie nicht
  erneut zu versuchen.
- Negativ-Wissen festhalten: plausibel klingende, aber nicht (mehr)
  existierende APIs explizit als "X existiert nicht – nutze Y" dokumentieren,
  genau dort, wo ein Agent danach suchen würde.
- Projektmanagement-Inhalte (Termine, Meeting-Notizen, organisatorische
  Fragen) bleiben draußen – die Instruktionen handeln vom Code.
- Wächst die Doku, lohnen eigene Dateien: ein Glossar (eine kurze,
  verlinkbare Definition pro Fachbegriff, am Code-Symbol verankert, beim
  Antreffen unbekannter Begriffe sofort ergänzt) und ein
  Invarianten-Register (tragende Contracts mit Aussage, Begründung,
  Enforcement-Ort – Code oder nur Konvention – und Symptom einer Verletzung;
  nach einem Bugfix, dessen Ursache ein unerzwungener Contract war, kommt
  ein Eintrag dazu).

## Memory

- Maschinenlokale Memory-Funktionen des Agents (etwa die Auto-Memory von
  Claude Code) nicht nutzen, auch wenn sie aktiv sind: Projekt-Wissen, das
  nur auf einer Maschine liegt, bringt im Team nichts. Niemals schreiben, du
  hättest dir etwas gemerkt, wenn es nur lokal gespeichert ist.
- Repo-bezogene Erkenntnisse gehören ins Repo (z.B. in dessen CLAUDE.md bzw.
  AGENTS.md) und werden committet.
- Globale Erkenntnisse (Arbeitsweise, Vorlieben, Umgebung) stattdessen dem
  User mitteilen, damit er sie in seiner persönlichen Konfiguration
  verankert.

<!-- TODO: Eigene Konventionen ergänzen (projektspezifischer Stil, Review-Regeln, ...) -->
<!-- claudete:general-conventions:end -->

## Branch-Schutz

Geht persönlichen Agent-Regeln vor, die neue Branches oder Pushes untersagen:

- Nie auf `main` committen, vorher einen Branch nach „Branches & Commits“ anlegen.
- Nach dem letzten Commit den Branch pushen und, falls es noch keinen gibt, den Pull Request gegen `main` nach „Pull Requests“ als Draft öffnen (`gh pr create --draft`).
- Einen Branch nur per Rebase auf `main` aktualisieren, `main` nie in einen Branch mergen. Danach ist Force-Push nur mit `--force-with-lease` erlaubt (bei Stack-Branches per `gh stack push`) und nur auf eigene, ungemergte Branches, nie auf `main`.
- Nie auf „Ready for review“ stellen, nie mergen.

## CI

Hintergrund und Entscheidungen: [docs/ci.md](docs/ci.md)

- Lokal dieselben Checks wie die CI ausführen:
  - App (`src/app`): `fvm flutter pub get --enforce-lockfile`, `fvm dart run build_runner build`, `fvm dart format --output=none --set-exit-if-changed .`, `fvm flutter analyze`, `fvm flutter test`.
  - Backend (`src/backend`): `dotnet format --verify-no-changes`, `dotnet test`, `dotnet restore --force -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904`.
  - Auth (`src/auth`): `npm ci`, `npm run check`.
- Generierte Dateien (`*.g.dart`, `*.gr.dart`, `*.freezed.dart`) werden nicht committet; CI erzeugt sie in der Composite-Action `.github/actions/setup-app` per build_runner.
- Funde ohne Fix unterdrücken: Backend per `<NuGetAuditSuppress>` im csproj, App (auch `src/app/Gemfile.lock`) per `[[IgnoredVulns]]` in `src/app/osv-scanner.toml`, Auth per `[[IgnoredVulns]]` in `src/auth/osv-scanner.toml`.
- Concurrency nur in `ci.yml` setzen. Ausnahme: `deploy-backend.yml` und jeder weitere Workflow, der Dev ändert, bekommt die feste Gruppe `deploy-dev` mit `cancel-in-progress: false`, der aufrufende `deploy-backend`-Job in `ci.yml` keine. `deliver-app.yml` ändert Dev nicht und bekommt keine Gruppe `deploy-dev`, nur sein Android-Job hat die feste Gruppe `google-play-upload` mit `cancel-in-progress: false`.
- Debug-Symbole und R8-Mappings lädt nur `build-app.yml` mit dem Input `deliver` hoch (der iOS-Job legt damit zusätzlich die IPA als Artifact für `deliver-app.yml` ab, der Android-Job das App-Bundle als Artifact `android-aab`), `ci.yml` setzt ihn nur bei Push auf `main`. `dart run sentry_dart_plugin` und sentry-cli nie lokal oder in Pull Requests aufrufen und die Gradle-Property `sentryUploadProguardMapping` dort nie setzen, auch nicht durch Agents (das Gradle-Plugin ruft sentry-cli in jedem Release-Build selbst auf, ohne die Property nur im Dry-Run mit `--no-upload`). `SENTRY_AUTH_TOKEN` liegt nur im Environment `development`, Jobs nennen es nur bei `deliver` und mit `deployment: false`.
- Release-Builds in `build-app.yml` behalten `--split-debug-info=build/debug-info` (gleich `symbols_path` im Abschnitt `sentry:` der `src/app/pubspec.yaml`) und laufen ohne `--obfuscate`.
- Jede Datei mit Secrets oder Schlüsseln, die ein Job in `build-app.yml` oder `deliver-app.yml` schreibt (auch indirekt über fastlane), löscht dessen letzter Step „Remove signing secrets“ (`if: always()`). Neue solche Dateien dort ergänzen.

## VS Code

Hintergrund und Entscheidungen: [docs/vscode.md](docs/vscode.md)

- Die Tasks in `.vscode/tasks.json` enthalten die lokalen Checks unter [CI](#ci) ohne `pub get`, `npm ci` und `dotnet restore`: Ändert sich dort ein Befehl, die Task im selben Commit anpassen.

## Aspire und Azure

Hintergrund und Entscheidungen: [docs/azure.md](docs/azure.md)

| Befehl | Agent |
| --- | --- |
| `aspire publish --environment azure-dev` (ohne CLI: `dnx --yes aspire.cli@<SDK-Version des AppHost> -- publish --apphost <csproj> --environment azure-dev --output-path <Ordner> --non-interactive`; ohne `--environment` läuft der AppHost als Production und schreibt Artefakte, die von Dev abweichen) | erlaubt |
| `aspire run`, `dotnet test` | nur, solange jede im Run-Mode referenzierte Azure-Ressource lokal als Container oder Emulator läuft (derzeit erfüllt) |
| `aspire deploy`, `aspire destroy`, `aspire do` mit Provisionierungs- oder Deploy-Steps, `gh workflow run deploy-backend.yml`, Re-run von Läufen auf `main` | nie, nur ein Mensch |
| `az containerapp exec`, `npm run create-user` gegen Azure | nie, nur ein Mensch |

- Jede neue Azure-Ressource, die eine App im Run-Mode referenziert, bekommt einen Container oder Emulator. Ausgenommen sind Ressourcen, die Aspire nur beim Publish anlegt (User-assigned Identities, Postgres-Entra-Admins, Container-Apps-Jobs).
- Ohne Werte für `better-auth-secret` und `auth-service-key` starten `auth` und die API bei `aspire run` und in Run-Mode-Tests nicht. Lokal als User-Secrets `Parameters:better-auth-secret` und `Parameters:auth-service-key` des AppHost (etwa `openssl rand -base64 32`), in Tests als Argumente `--Parameters:better-auth-secret=...` und `--Parameters:auth-service-key=...`.
- `aspire run` und `dotnet test` brauchen Node in der Version aus `src/auth/.nvmrc` auf dem PATH.
- `Azure:*`-Werte nur in `src/backend/Murmli.Backend.Aspire/appsettings.azure-<name>.json`, nie in andere appsettings oder User Secrets.
- Subscription-ID und andere Azure-IDs nie ins Repo, im GitHub-Environment `development` nur als Secrets, nie als Variablen. `deploy-backend.yml` reicht die Subscription-ID als `Azure__SubscriptionId` an den AppHost.
- In `deploy-backend.yml` behalten: `shell: bash` am Step „Deploy“ (nur damit läuft das Skript mit `pipefail`), bis zu zwei Wiederholungen von `aspire deploy`, Anmeldung über die `az`-Session mit Token-Abruf direkt nach dem Login, Resource-Strings ohne Schrägstrich am Ende. Braucht Aspire eine weitere Audience, sie dort ergänzen (sonst AADSTS700024). `~/.aspire/deployments` nie cachen oder hochladen.
- Die Version von `Aspire.AppHost.Sdk` nur im `Sdk`-Attribut des AppHost-csproj pflegen.
- Neue Rollenzuweisungen im AppHost und neue Resource Provider erst einbauen, wenn ein Mensch sie in Azure vorbereitet hat (Bedingung erweitern bzw. Provider registrieren).
- Die Registry im RBAC-Modus lassen.

### Ressourcen

- Pro Service ein Ordner im AppHost (`src/backend/Murmli.Backend.Aspire`) mit `Extensions.cs` und `Add<Service>`-Extension, `AppHost.cs` verdrahtet nur.
- Die Ressourcennamen `env`, `postgres`, `murmli`, `api`, `auth`, `backend-identity`, `product-import`, `app-insights`, `better-auth-secret` und `auth-service-key` und das Naming-Schema der Umgebung nie ändern.
- Jede App mit Datenbankzugriff bekommt `backend-identity` (`WithAzureUserAssignedIdentity`).
- Nie verwenden: Aspire-Dashboard in Azure, ACA Express, Dedicated Workload Profiles, Private Endpoints, geplante Wartungsfenster.
- Container-Apps-Probes erst beim ersten konkreten Bedarf ergänzen. Kein ServiceDefaults-Template, Telemetrie siehe „Telemetrie“.

## Datenbank

Hintergrund und Entscheidungen: [docs/database.md](docs/database.md)

- Den Context nur per `builder.AddMurmliDbContext()` registrieren.
- Datenzugriff mit EF Core, eigenes SQL nur, wo EF Core es nicht kann. Ausnahme: Der Sync-Kern nutzt eigenes SQL (`ExecuteSql`, `FromSql`) nur für feste Anweisungen auf `sync.*` und `identity.users`: den Claim (`ON CONFLICT DO NOTHING`), die Zeilensperren der Bereiche (`FOR NO KEY UPDATE`), das Anlegen des persönlichen Bereichs und `lock_timeout` ([docs/database.md](docs/database.md#abgleich)). Für die künstlichen Datentypen der Tests ersetzt `ApiHost` per `ConfigureDbContext` nur den `IModelCustomizer` und legt ihre Tabellen im Schema `synctest` per handgeschriebenem DDL an, nie per Migration.
- In `OnConfiguring` keine Optionen ändern (auch kein `EFCore.NamingConventions`), snake_case explizit in den `IEntityTypeConfiguration`-Klassen setzen.
- Pro Fachbereich ein Schema mit kleingeschriebenem Namen per `ToTable(name, schema)`, `__EFMigrationsHistory` und Extensions in `public` lassen. In Raw SQL Tabellen immer mit Schema nennen (`products.products`).
- Das Schema `better-auth` gehört Better Auth: nur `auth` migriert es, EF Core und die API greifen nie darauf zu.
- EF-Core-Pakete (auch `Microsoft.EntityFrameworkCore.Design`) bis [#14](https://github.com/Murmli/MurmliV2/issues/14) auf 10.x halten, die direkte Referenz auf `Microsoft.EntityFrameworkCore.Relational` nicht entfernen.
- Migration anlegen (in `src/backend`): `dnx --yes dotnet-ef@<EF-Version> -- migrations add <Name> --project Murmli.Backend.Data`.
- `MigrateAsync` nur im Job und beim Start der API aufrufen (`app.MigrateDatabaseAsync()` nach `Build()`, vor `RunAsync()`). Optionen, die der Context dafür braucht (das Retry-Budget aus `DatabaseOptions`), liest `configureNpgsql` über den übergebenen `IServiceProvider` aus `IOptions<T>`, nie per `Configuration.Get<T>()`: `ValidateOnStart` prüft erst in `RunAsync()`, nach der Migration. Migrationen additiv und nullable halten, solange eine ältere Version von API oder Job noch laufen kann. Migrationen nie selbst squashen: Ob und wann sie in ein neues `InitialCreate` zusammengefasst werden, entscheidet ein Mensch.
- Smart Enums (`[SmartEnum<string>]`) per `HasConversion(status => status.Key, key => <Typ>.Get(key))` als `text` speichern, nicht über das Thinktecture-EF-Paket.
- Produktsuche pro Wort per `EF.Functions.ILike(spalte, "%" + wort + "%", "\\")` auf Name oder Marke, jedes Wort als eigener Parameter, mit selbst escaptem `%`, `_` und `\`. Mindestens ein Wort braucht 3 Zeichen, kürzere filtern nur mit. Nie `Contains`, `ToLower().Contains` oder ein `All` über ein Array von Wörtern (wird zu `unnest`, ohne Index).
- Neue Postgres-Extensions in `azure.extensions` in `Database/Extensions.cs` des AppHost eintragen (`DependsOn` behalten), nie im Portal.

## API

Hintergrund und Entscheidungen: [docs/api.md](docs/api.md)

- Endpunkte und Middleware nur in `UseApi` registrieren (`Program.cs` und `ApiHost` der Tests richten die Pipeline nur über `UseApi()` ein, `ApiHost` mappt danach höchstens Test-Endpunkte), neue Endpunkte in die Gruppe `/v1`, nie direkt auf `application`. Ohne Version bleiben nur `/health` und das OpenAPI-Dokument. `UseExceptionHandler()` bleibt nach `UserTelemetryMiddleware`, sonst fehlt `UserId` an unbehandelten Exceptions. Kein `IExceptionHandler` registrieren, ohne `ExceptionHandlerOptions.SuppressDiagnosticsCallback` anzupassen, sonst fehlt für die Exceptions, die er behandelt, der Log `UnhandledException` samt `UserId`.
- Bis zum ersten Live-Gang alles in `v1` ändern, auch inkompatibel. Danach innerhalb einer Version nur rückwärtskompatibel ändern (neue Endpunkte, neue Felder in Antworten), alles andere bekommt eine neue Version.
- Kein Asp.Versioning einführen, bevor es .NET 11 unterstützt ([#68](https://github.com/Murmli/MurmliV2/issues/68)), und keine Version per Header oder Query.
- Fehler, auf die Clients eigens reagieren, als Problem Details mit `type` `tag:murmli.de,2026:<code>` aus `ProblemTypes`, geschrieben per `TypedResults.Problem` (auch ohne JSON im `Accept`-Header mit Body). Clients reagieren auf `type`, nie auf den bloßen Status.
- Keine Middleware zur Antwortkompression (`AddResponseCompression`, `UseResponseCompression`). Komprimiert wird nur in `/v1/sync` und nur eine Antwort, in der mindestens ein Block einen vollständigen Stand hat ([docs/sync.md](docs/sync.md#kompression)); jede andere Antwort bleibt wegen BREACH unkomprimiert.

## Authentifizierung

Hintergrund und Entscheidungen: [docs/authentication.md](docs/authentication.md)

- Jeder Endpunkt ist nur mit Anmeldung erreichbar (Default- und Fallback-Policy). Ausnahmen per `AllowAnonymous()` nur für `/health`, das OpenAPI-Dokument und für Endpunkte, deren Issue ausdrücklich verlangt, dass sie anonym erreichbar sind, nie aus eigenem Ermessen.
- Die App spricht mit Better Auth nur zum Anmelden, Abmelden und Holen bzw. Erneuern des JWT, alles andere läuft über die API.
- Die API greift nie auf die Tabellen von Better Auth zu (kein EF-Mapping, kein SQL), sie nutzt immer die HTTP-API von `auth`.
- Nutzerdaten hängen an `identity.users.id`, nie an der Better-Auth-ID. Jede neue Art von Nutzerdaten gehört in die Löschung: in Postgres per Fremdschlüssel mit `ON DELETE CASCADE` auf `identity.users`, abgeglichene Datensätze über ihren Bereich (`sync.scopes.owner_account_id`); Ausnahme `sync.member_entries.account_id` mit `SET NULL` ([#131](https://github.com/Murmli/MurmliV2/issues/131)); außerhalb von Postgres ausdrücklich in `UserRegistry.DeleteAsync` vor dem Aufruf von `auth`. Ausgenommen ist Telemetrie mit der eigenen Id, sie läuft mit ihrer Aufbewahrungsfrist aus.
- `sub`, E-Mail und Token nie selbst loggen und nie in Span-Attribute schreiben. Den Nutzer kennzeichnet in der Telemetrie nur die eigene Id, immer unter `UserId` (`LogProperties.UserId`), nie unter `enduser.id`.
- `BETTER_AUTH_SECRET` kommt nur über den Secret-Parameter `better-auth-secret` und wird nie geändert, Rotation nur über `BETTER_AUTH_SECRETS`.
- `session.expiresIn` in `src/auth/src/authentication/auth.ts` bleibt 30 Tage, die App verlässt sich darauf (Hinweis beim Abmelden ohne Verbindung).
- `jwks.keyPairConfig.alg` bleibt ES256, `user.deleteUser` von Better Auth bleibt aus.
- `@better-auth/core` bleibt auf derselben Version wie `better-auth`.
- Was `auth` zur Laufzeit importiert, gehört in `dependencies`, das Image enthält keine `devDependencies`. `--omit=dev` nur im Publish-Mode setzen (`AddAuth()`), im Run-Mode entfernte Aspires Installer sonst Biome und tsc aus `src/auth/node_modules`.
- `OpenApi:Enabled` nur in `appsettings.Development.json` und `appsettings.azure-dev.json` des AppHost setzen, sonst ist das OpenAPI-Dokument in weiteren Umgebungen ohne Anmeldung erreichbar.
- `Apple:BundleIdentifier` und `Google:WebClientId` nur in `appsettings.Development.json` und `appsettings.azure-<name>.json` des AppHost setzen, nie in `appsettings.json` (eine neue Umgebung erbte sonst die Werte von Dev).
- Sign in with Apple und Google nur über das ID-Token (`POST /api/auth/sign-in/social` mit `idToken`), ohne Client Secret, ohne Scopes bei Apple. In `auth` `mapProfileToUser` behalten: Platzhalter-E-Mail für beide, bei Google zusätzlich leerer Name und kein Bild (ohne Platzhalter lehnt Better Auth jede Anmeldung mit Apple ab, mit `profile.email` oder ohne die Google-Felder speicherte es Adressen, Namen und Bilder). Ebenso die `databaseHooks` für `account` behalten, die `idToken` auf `NULL` setzen.
- Tests stellen eigene Tokens aus (`TestTokens`) und ersetzen `auth` durch `AuthServiceHttpMessageHandler`. Echte Tokens nur im End-to-End-Test `AuthenticationTests` gegen das lokal gestartete `auth`, kein Test spricht mit Azure.
- `TokenValidationParameters` im Code nie ersetzen, die Werte kommen aus der Bindung von `Authentication:Schemes:Bearer`.

## Telemetrie

Hintergrund und Entscheidungen: [docs/telemetry.md](docs/telemetry.md)

- Jeder .NET-Host ruft `builder.AddTelemetry()` nach dem Hinzufügen aller Konfigurationsquellen und vor `Build()` auf (es liest `Telemetry:RecordSensitiveData` beim Aufruf), jede App-Ressource im AppHost bekommt `.WithTelemetry(applicationInsights)`.
- Application Insights und die tägliche Obergrenze des Workspaces nur über `AddApplicationInsights` in `ApplicationInsights/Extensions.cs` des AppHost, den Wert der Obergrenze pro Umgebung nur in `appsettings.azure-<name>.json` (`LogAnalytics:DailyQuotaGb`). Der AppHost fügt Application Insights nur im Publish-Mode hinzu, nie im Run-Mode, sonst provisioniert Aspire es bei `aspire run` und `dotnet test` in Azure.
- Eigene `ActivitySource`s und `Meter` heißen `Murmli.*`.
- Keine Gesundheitsdaten in Logs, Span-Attributen, Span-Namen oder URLs.
- SQL-Werte nur als Parameter, nie in SQL einsetzen (kein `FromSqlRaw` mit zusammengesetztem SQL).
- Sensible Daten (LLM-Inhalte, SQL-Parameterwerte) nur über `Telemetry:RecordSensitiveData` (`IConfiguration.RecordsSensitiveData()`), nie fest im Code.
- Kein `ConfigureHttpClientDefaults` mit Resilience-Handlern.
- Hosts, die außerhalb von `RunAsync()` arbeiten (Job-Hosts, die API mit der Migration beim Start), entsorgen den Host selbst (`using var host` bzw. `await using var app`), sonst geht die zuletzt gepufferte Telemetrie verloren.
- Den Sampler nur in `AddTelemetry()` setzen und nach allen Exportern registrieren, er verwirft `/health` samt Kind-Spans.
- `auth` startet die Telemetrie in `src/auth/src/infrastructure/telemetry.ts`, `src/auth/src/main.ts` lädt sie vor allen anderen Modulen. Die OpenTelemetry-Pakete in `src/auth` bleiben auf der SDK-2.x-Linie, bis `@azure/monitor-opentelemetry-exporter` SDK 3.x unterstützt.
- `fetch`-Spans von `auth` bleiben ohne Query: Der `startSpanHook` in `telemetry.ts` bleibt und darf nie werfen, sonst steht die volle URL im Span. Header nie als Span-Attribute aufnehmen (`headersToSpanAttributes`).

## Produktimport

Hintergrund und Entscheidungen: [docs/product-import.md](docs/product-import.md), vor Änderungen an Reader, Mapper, Download oder Abgleich lesen.

- Den vollen Export nie lesen: AppHost nur mit `ProductImport__ProductLimit=100` am Startbefehl starten (nie global setzen), dann `aspire resource product-import start`.
- Nach dem Start in der Logzeile `Starting the product import run … with the product limit 100` das Limit prüfen, fehlt es, die Ressource sofort stoppen.
- In Tests den Export als Inline-Fixture über einen `HttpMessageHandler`-Stub liefern.
- Nährwerte nur aus `value` lesen, nie aus `value_computed`, und nur bei `preparation` `as_sold`.

## Abgleich

Hintergrund und Entscheidungen: [docs/sync.md](docs/sync.md)

- Abgeglichene Tabellen (`sync.scopes`, `sync.member_entries`, `sync.tombstones`, `sync.processed_commands` und jede Tabelle eines Datentyps) ändert nur `Murmli.Backend.Sync` (`SyncProcessor`, `SyncAccountLifecycle`). Einzige Ausnahme: per `ON DELETE CASCADE` ganze Bereiche löschen (Wurzel-Datensatz, Kontolöschung) sowie die `sync.processed_commands` eines Kontos über `identity.users`, und `sync.member_entries.account_id` per `ON DELETE SET NULL` ([#131](https://github.com/Murmli/MurmliV2/issues/131)). Kein `SaveChanges`, kein `ExecuteUpdate`/`ExecuteDelete`, kein eigenes SQL und keine Löschweitergabe zwischen Datensätzen daran vorbei. Tests legen abgeglichene Daten nur über `/v1/sync` an. Ausnahmen nur in `DatabaseAdministration`: bis [#129](https://github.com/Murmli/MurmliV2/issues/129) Mitgliedseinträge weiterer Konten samt Erhöhung der Version ihres Bereichs, außerdem als Buchhaltung die Untergrenze eines Bereichs (`sync.scopes.tombstone_floor`).
- `sync.database_identity` ändert nur ein Mensch nach dem Einspielen einer Sicherung sowie die Tests (`DatabaseAdministration`).
- Jede Änderung an einem Datensatz eines Datentyps, seiner Löschmarkierung oder einem Mitgliedseintrag erhöht die Version ihres Bereichs unter dessen Zeilensperre in derselben Transaktion.
- Sperren immer erst auf den persönlichen Bereich, dann auf genau einen weiteren Bereich. Vorgänge über mehrere Bereiche je Bereich in einer eigenen Transaktion. Was den neuesten Stand braucht, liest eine eigene Anweisung nach der Sperre.
- Eigene Transaktionen nur als Einheit der Execution Strategy, je Kommando eine, schreibend in READ COMMITTED, der Lesepfad in REPEATABLE READ.
- Einheiten des Kerns lesen ohne Tracking (`AsTracking()` nur für den gesperrten Bereich und den Zieldatensatz), leeren den Change Tracker zu Beginn jedes Versuchs, laufen mit `AutoSavepointsEnabled = false` und löschen Wurzeln per `ExecuteDeleteAsync` auf `sync.scopes`, nie per `Remove`. Datenbank-Exceptions fangen sie nie, ohne sie weiterzuwerfen: Ohne Savepoints rollt `CommitAsync` nach einem gescheiterten `SaveChanges` sonst still alles zurück.
- Eigener Code loggt nie Inhalte abgeglichener Datensätze und schreibt sie nie in Span-Attribute oder Metrik-Tags. SQL-Parameterwerte erscheinen nur unter `Telemetry:RecordSensitiveData` (Dev-Ausnahme aus [#138](https://github.com/Murmli/MurmliV2/issues/138)).
- Zeiten des Abgleichs nur aus dem injizierten `TimeProvider`, auf Millisekunden gekürzt, nie `now()` oder `DateTime.UtcNow`. Zeitgrenzen des Betriebs (Budget, Frist) laufen in Echtzeit.
- Jede Tabelle eines Datentyps hat `id`, `scope_id` mit Fremdschlüssel `ON DELETE CASCADE` auf `sync.scopes`, `version` mit Index `(scope_id, version)` und je Feldgruppe `<gruppe>_changed_at`. Ihre Entität erbt von `SyncRecord`, das selbst nie gemappt wird (kein `Entity<SyncRecord>()`, kein `DbSet`, keine Navigation dieses Typs). Verweise auf Personen zeigen auf `sync.member_entries` mit `DeleteBehavior.ClientNoAction`. Einen Datentyp nur nach [docs/sync.md](docs/sync.md#datentyp-anbinden) anbinden, samt Fällen in `src/sync-cases/<bereich>/`; einen neuen Ordner dort auch in die Ordner aufnehmen, die `SyncEndpointsCaseCollectionTests` liest.
- Antworten des Abgleichs enthalten nie Konto-IDs; Datensätze nennen Personen nur über Mitglieds-IDs, die der Server setzt (Füllungen aus `SyncServerFill` oder `Evaluate` mit `ActingMemberId`), nie über Werte vom Gerät.
- Gespeicherte Gründe, Ursachen, Arten und Rollen als Text lesen, nie mit `Get`.
- Regeln zur Schreibberechtigung ([Murmli/BackOffice#17](https://github.com/Murmli/BackOffice/issues/17)) und Messwerte (Lasttests, Simulation, Kapazität, [Murmli/BackOffice#18](https://github.com/Murmli/BackOffice/issues/18)) gehören ins BackOffice, nie in dieses Repo, auch nicht in Kommentare oder Testausgaben.

## App

Hintergrund und Entscheidungen: [docs/app.md](docs/app.md)

- Die App nur per `fvm flutter run` bzw. `fvm flutter build` starten und bauen, Flavor per `--flavor` (ohne gilt `development`), nie über das Xcode-Scheme `Runner`.
- Flavor- und Build-Modus-abhängiges Verhalten nur über `flavorProvider` bzw. `debugBuildProvider` (`src/app/lib/app/core/build_mode/debug_build_provider.dart`), nie `appFlavor` oder `kDebugMode` direkt lesen (Compile-Zeit-Konstanten, in Tests nicht überschreibbar).
- Flavor-Namen klein und ohne `-`. Ein neuer Flavor braucht einen gleichnamigen Wert im Enum `Flavor` (`src/app/lib/app/core/flavor/flavor.dart`) samt Fällen in `AppEndpoints.of`, im DSN-Switch von `CrashReporting` (`src/app/lib/app/core/crash_reporting/crash_reporting_provider.dart`), im Flavor-Switch von `SignInOptions` (`src/app/lib/app/features/onboarding/widgets/sign_in_options.dart`) und im Switch der Google-`serverClientId` (`_googleServerClientIdOf` in `src/app/lib/app/core/authentication/signed_in_account_provider.dart`), einen Eintrag in `productFlavors` (`android/app/build.gradle.kts`) mit `manifestPlaceholders["appLabel"]` (und `applicationIdSuffix`, wenn er eine andere ID als `de.murmli.app` braucht), die iOS-Configurations `Debug-`, `Release-` und `Profile-<flavor>` als Kopien von `Debug`, `Release` und `Profile`, die im Target `Runner` jeweils `PRODUCT_BUNDLE_IDENTIFIER` und `APP_DISPLAY_NAME` setzen, ein geteiltes Scheme `<flavor>` als Kopie von `Runner.xcscheme` mit diesen Configurations und einen Eintrag unter `flavors` in `src/app/flutter_adaptive_studio.yaml` samt `generate --flavor <flavor>` ([docs/app.md](docs/app.md#icons-und-splash)). Baut die CI den Flavor, braucht er zusätzlich einen Eintrag in der Signing-Map von `prepare_signing` in `src/app/fastlane/Fastfile` sowie eine App ID im Developer-Portal mit der Capability „Sign In with Apple“ als eigene primäre App ID, einen App-Eintrag in App Store Connect und eine App in der Play Console samt der Berechtigung „Release apps to testing tracks“ für den Service-Account `google-play-upload` sowie im Google-Cloud-Projekt der Umgebung Android-Clients für seine ID mit den SHA-1 aller App-Signaturschlüssel (auch der früheren) und des Upload-Schlüssels ([docs/authentication.md](docs/authentication.md#sign-in-with-google)) (nur ein Mensch).
- Launcher-Icons und Splash nur per `flutter_adaptive_studio` (dev_dependency mit fester Version, `fvm dart run flutter_adaptive_studio`, [docs/app.md](docs/app.md#icons-und-splash)) aus `src/app/flutter_adaptive_studio.yaml` neu erzeugen, die Ausgabe unter `android/` und `ios/` nie von Hand ändern. Danach `lib/fas_splash.g.dart` und `ios/Runner.xcodeproj/project.pbxproj.bak` löschen. Flavor-Einträge behalten `play_store: false` und `splash: null` (unter `android` und `ios`), die Basis `round: false`.
- Keine Klartext-Ausnahmen in App Transport Security oder der Network Security Config, solange lokaler Verkehr nur über `dart:io` läuft.
- Requests an die eigene API nur über `apiClientProvider` (`src/app/lib/app/core/api/api_client_provider.dart`) und mit Pfad ab `/` (`/v1/...`), nie mit voller URL, Requests an `auth` nur über `authClientProvider` (`src/app/lib/app/core/authentication/auth_client_provider.dart`) ohne die Interceptors der API. Code, der nur Requests an die eigene API betreffen darf (etwa Token oder `traceparent`), vergleicht die `origin` von `RequestOptions.uri` mit der von `appEndpointsProvider` und verlässt sich nicht auf die Instanz. Den `Authorization`-Header setzt nur `AccessTokenInterceptor` in `apiClientProvider`, Aufrufer nie selbst.
- Fehlgeschlagene Requests an die eigene API meldet `apiClientProvider` selbst (Log mit Trace-ID, Fehlerdialog mit der Trace-ID als Support-Code), Aufrufer zeigen dafür keinen eigenen Dialog. Request-Logs nie mit Body, Headern oder Query, deshalb kein `talker_dio_logger`. Interceptors, die Fehler selbst behandeln (etwa die Token-Erneuerung), kommen in `apiClientProvider` zwischen `ApiTraceInterceptor` und die Meldung an `failedApiRequestTraceIdProvider`.
- Jede neue Art von Daten auf dem Gerät gehört in die Löschung des Kontos (`AccountActions.delete` in `src/app/lib/app/features/settings/providers/account_actions_provider.dart`): Neue Schlüssel in `PreferenceKeys.all` entfernt sie automatisch, Provider, die solche Werte zwischenspeichern, invalidiert sie ausdrücklich.
- Zeitabhängige Logik der App liest die Zeit nur über `appClockProvider`, nie `DateTime.now()` (der Provider heißt nicht `clockProvider`, sonst verdeckt die generierte Funktion `clock` den Getter aus `package:clock`).
- Anmeldung nur über `signedInAccountProvider`. Session-Token und JWT liegen nur dort, den sicheren Speicher (`flutter_secure_storage`) nutzt nur er, das JWT wird nie gespeichert. Im Flavor `local` nur E-Mail und Passwort, weder Apple noch Google, E-Mail und Passwort nie außerhalb von `local`. Sonst Apple nur auf iOS und Google nur auf Android. Anmeldedaten von Apple und Google, Nonces, Tokens, E-Mail und Passwort nie loggen oder an Sentry melden.
- Google nur über `google_sign_in_android` und `google_sign_in_platform_interface` (`GoogleSignInPlatform.instance`), nie `google_sign_in` (es linkt das GoogleSignIn-SDK in die iOS-App).
- Sentry nur über `CrashReporting` starten und stoppen, `SentryFlutter.init` und `Sentry.close` nur in `CrashReporter` (`src/app/lib/app/core/crash_reporting/crash_reporter.dart`), Sentry-Optionen nur in `configureCrashReporting` (`src/app/lib/app/core/crash_reporting/crash_reporting_options.dart`). Den Nutzer kennzeichnet in Sentry nur die eigene Konto-Id, gesetzt von `CrashReporting` per `CrashReporter.setUserId`. Kein Test startet die nativen SDKs (`createContainer` ersetzt `crashReporterProvider`).
- An Sentry gehen nur Fehler, Abstürze, ANRs und Sitzungen (Release Health): kein `sentry_dio`, kein `SentryHttpClient`, kein `sentry_logging`, kein `Sentry.logger`, kein Talker-Observer, der an Sentry meldet. `enablePrintBreadcrumbs`, `recordHttpBreadcrumbs`, `captureFailedRequests` und `sendClientReports` bleiben aus, `tracePropagationTargets` leer, Replay, Tracing und `sendDefaultPii` ungesetzt, `enableAutoSessionTracking` beim Standard (an).
- Sentry meldet Fehler mit ihrem Text (`toString()`), Fehler aus `FlutterError.onError` zusätzlich mit Kontext und Diagnose ihrer `FlutterErrorDetails` (`context`, `informationCollector`). Fehler, deren Text oder Diagnose Nutzereingaben, URLs mit Query oder Bodies enthalten kann, ersetzen den Text wie `ApiTraceInterceptor` bei `DioException` bzw. lassen solche Angaben aus der Diagnose weg.
- Nach Updates von `sentry_flutter` prüfen, ob die nativen SDKs ungesendete Berichte noch in den Cache-Verzeichnissen aus `CrashReporter` ablegen ([docs/app.md](docs/app.md#entscheidungen)).
- `default-flavor` in `pubspec.yaml` bleibt `development`. Die CI baut nur `development`, übergeben per Input `flavor` von `build-app.yml` in `ci.yml`, `flutter test` läuft ohne `--flavor`.

## Apple

Hintergrund und Entscheidungen: [docs/apple.md](docs/apple.md)

- `build-app.yml` baut iOS immer signiert: `bundle exec fastlane ios prepare_signing flavor:<flavor>`, dann `flutter build ipa`, mit `deliver` legt es die IPA als Artifact ab. Hochladen per `bundle exec fastlane ios upload` nur in `deliver-app.yml` (aus `ci.yml` nur bei Push auf `main`, nach allen Checks, parallel zu `deploy-backend`). Kein `build_app`/gym und kein `flutter build ios --config-only`.
- `bundle exec fastlane` nie lokal oder als Agent ausführen, nur die CI ruft es auf.
- Zertifikate, API-Schlüssel, App IDs, Provisioning-Profile, App-Einträge und Tester legt nur ein Mensch an oder löscht sie. Einzige Ausnahme: das Profil des Flavors, das `sigh` in `prepare_signing` anlegt, wenn es fehlt.
- `APPLE_*` nur als Repository-Secrets, `ci.yml` reicht sie namentlich an `build-app.yml`, an `deliver-app.yml` nur die drei `APPLE_APP_STORE_CONNECT_*`.
- Kein `DEVELOPMENT_TEAM` im Xcode-Projekt committen, auch nicht nach dem Signieren für ein Gerät.
- Entitlements nur in `src/app/ios/Runner/Runner.entitlements` (gilt für alle Configurations). Ein neues Entitlement erst, wenn die Capability an der App ID aktiv und das Profil neu erzeugt ist ([docs/apple.md](docs/apple.md#neue-capability)).
- `src/app/Gemfile.lock` nie von Hand ändern, nur mit dem Docker-Befehl aus [docs/apple.md](docs/apple.md#fastlane-aktualisieren) neu erzeugen (Ruby-Version aus `src/app/.ruby-version`).

## Google Play

Hintergrund und Entscheidungen: [docs/google-play.md](docs/google-play.md)

- `build-app.yml` signiert Android immer mit dem Upload-Schlüssel: den Keystore nur aus `ANDROID_UPLOAD_KEYSTORE` nach `$RUNNER_TEMP` schreiben, Pfad und Passwort nur als Gradle-Properties `murmliUploadKeystoreFile` und `murmliUploadKeystorePassword` per `ORG_GRADLE_PROJECT_*` am Build-Step übergeben. Kein `key.properties`, Keystore und Passwörter nie ins Repo, auch nicht in `src/app/android/gradle.properties`.
- In `src/app/android/app/build.gradle.kts` den Rückfall auf den Debug-Schlüssel ohne beide Properties und den Abbruch bei nur einer behalten. Den Step „Check app bundle“ in `build-app.yml` behalten: Er lässt den Job bei App-Bundles scheitern, die nicht mit dem Upload-Schlüssel signiert sind, und liefert Release und Dist für Sentry sowie den Paketnamen für den Upload.
- bundletool nur mit fester Version und SHA-256 laden.
- Hochladen nach Google Play per `bundle exec fastlane android upload` nur in `deliver-app.yml` (aus `ci.yml` nur bei Push auf `main`, nach allen Checks, parallel zu `deploy-backend`). Den Paketnamen bekommt die Lane nur aus dem Output `android-application-id` von `build-app.yml`, nie fest im Fastfile oder über eine Zuordnung von Flavor zu Paketname.
- `ANDROID_*` nur als Repository-Secrets, `ci.yml` reicht sie namentlich an `build-app.yml`. `GOOGLE_PLAY_SERVICE_ACCOUNT_KEY` (Base64) nur im Environment `development`, `ci.yml` reicht ihn namentlich nur an `deliver-app.yml`, dessen Job Android das Environment mit `deployment: false` nennt.
- Den Schlüssel des Service-Accounts nie in eine Datei schreiben, fastlane bekommt ihn per `json_key_data`, im Speicher aus der Umgebungsvariable dekodiert. Sonst gilt die Regel zu „Remove signing secrets“ unter [CI](#ci).
- Apps, Tester, den Upload-Schlüssel, den Service-Account samt Schlüssel und Berechtigungen sowie Releases in der Play Console legt nur ein Mensch an oder ändert sie, ausgenommen die Releases im internen Test aus `deliver-app.yml`. Der Service-Account behält nur die App-Berechtigung „Release apps to testing tracks“ für die Apps der Flavors, die die CI baut (derzeit „Murmli Dev“). Der erste Upload jeder App läuft von Hand.
