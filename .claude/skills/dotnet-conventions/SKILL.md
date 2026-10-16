---
name: dotnet-conventions
description: Bei .NET-/C#-Entwicklung nutzen – Projekte, ASP.NET Core, Tests, NuGet. Enthält die .NET-Konventionen dieses Projekts.
---

# .NET-Konventionen

Gelten, sofern das Repo an anderer Stelle nichts Abweichendes definiert.
Verwendet das Repo bereits andere Frameworks oder Bibliotheken (z.B. NUnit
statt xUnit, Moq statt NSubstitute), gilt das als definierte Abweichung –
die Defaults hier greifen für neue Projekte und neue Testsuiten.

## Sprache & Stil

- Nullable Reference Types aktiviert; Standard ist non-nullable. `?` nur für
  optionale Parameter, nullable DB-Felder und Lookups, die nichts finden
  dürfen; `null!` nur in Testcode.
- Immer geschweifte Klammern bei `if`/`else`/`for`/`foreach`/`while`/`do`,
  auch bei einzeiligen Bodies.
- Ein Top-Level-Typ pro Datei, Dateiname = Typname. Ausnahmen: verschachtelte
  Typen und gleichnamige Typen, die sich nur in der Zahl der generischen
  Typparameter unterscheiden (`Foo` und `Foo<T>`).
- File-scoped Namespaces; keine `#region`s.
- `is null` statt `== null`; .NET-Builtins bevorzugen; moderne C#-Features
  nutzen (Collection Expressions; `field`-Keyword für Lazy-Backing-Fields
  ab C# 14).
- Formatierung via CSharpier (`dotnet csharpier format .`), sofern im Repo
  eingerichtet; sonst dem bestehenden Format folgen.

## Enums

- Für feste Wertemengen mit Verhalten oder persistierbaren Status/State
  Thinktecture SmartEnums (`[SmartEnum<string>]`) statt normaler Enums;
  Methoden via `[UseDelegateFromConstructor]`. Nicht für simple Booleans,
  offene Wertemengen oder häufig wechselnde Sets.

## Async

- `Task<T>` für I/O und Aufrufe anderer async-Methoden; `ValueTask<T>` nur
  für Methoden, die häufig synchron abschließen (Cache, Vorberechnetes) oder
  perf-kritisch sind. Im Zweifel `Task<T>`.
- Jede async-Methode nimmt `CancellationToken cancellationToken = default`
  und reicht das Token durch.

## Zeit

- `TimeProvider` (per Konstruktor injiziert) statt `DateTime.UtcNow` bzw.
  `DateTimeOffset.UtcNow`; in Tests `FakeTimeProvider` (`SetUtcNow`,
  `Advance`).
- `DateTime` nie als `.Ticks` serialisieren (plattformspezifisch,
  cross-language-feindlich) – als Zahl immer Unix-Millisekunden.

## Logging

- `[LoggerMessage]`-Partial-Methoden statt direkter
  `logger.LogInformation(...)`-Aufrufe; alle `[LoggerMessage]`-Methoden
  einer Klasse liegen in einer eigenen Partial-Datei `<Klasse>.Log.cs`
  neben der Klasse.
- Message-Template-Platzhalter aus gemeinsamen Konstanten, nie als
  Inline-Literale – dasselbe Konzept loggt immer unter demselben Namen.
- Jede `[LoggerMessage]` ab Level `Information` bekommt eine eindeutige
  Event-ID aus einem zentralen Register (z.B. eine `LogEvents`-Klasse;
  anlegen, falls das Repo noch keins hat) plus `EventName`; `Debug`/`Trace`
  dürfen sie weglassen.

## Tests

- xUnit mit `Assert.*`; Fluent-Assertion-Bibliotheken sind verboten.
- NSubstitute als einzige Mocking-Bibliothek (kein Moq, kein FakeItEasy).
- Testmethoden sind `async Task`, nie `ValueTask`. In Repos mit xUnit v3
  async-Aufrufen `TestContext.Current.CancellationToken` mitgeben (die API
  gibt es unter xUnit v2 nicht, dort entfällt das).
- Naming: `{MethodName}_{Scenario}_{ExpectedResult}`.
- Parametrisierte Fälle per `[Theory]` mit `[InlineData]`/`[MemberData]`
  statt duplizierter Testmethoden.
- Tests mit `dotnet test` vor jedem Commit.

<!-- TODO: Projektspezifische Konventionen ergänzen (Architektur, Analyzer, ...) -->
