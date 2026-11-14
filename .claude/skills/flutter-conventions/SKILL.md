---
name: flutter-conventions
description: Bei Flutter/Dart-Entwicklung nutzen – Widgets, State, Tests, pub. Enthält die Flutter-Konventionen dieses Projekts.
---

# Flutter-Konventionen

Kurzfassung der offiziellen Flutter-AI-Rules:

- Null-safe; `!` nur, wenn Nicht-Null garantiert ist
- Kleine, komponierte Widgets: private Widget-Klassen statt Helper-Methoden,
  `const`-Konstruktoren wo möglich, keine teure Arbeit in `build()`,
  `ListView.builder` für lange Listen
- State: Built-in-Lösungen (`ValueNotifier`/`ChangeNotifier` mit Buildern,
  Streams/Futures); Dritt-Packages nur auf explizite Anforderung
- Routing: go_router für deep-linkbare Navigation, `Navigator` für
  kurzlebige Dialoge
- `package:flutter_lints` in `analysis_options.yaml`; Tests mit
  `flutter_test`/`package:test`, Fakes vor Mocks
- Vollständige Rules: https://github.com/flutter/flutter/blob/main/docs/rules/rules.md

## Projekt-Konventionen

- Styling kommt aus dem Theme (`lib/app/core/theme/app_theme.dart`): Widgets nutzen Material-Widgets ohne Instanz-Styles (`FilledButton`, `OutlinedButton`, `ListTile`, `Chip`, `Divider`) und Textrollen über `TextTheme.of(context)`. Fehlt etwas, wird das Component-Theme erweitert, nicht das Widget gestylt.
- Konstanten nur für Layout und Custom-Elemente: Abstände aus `AppSpacing` (4px-Raster), Animationsdauern aus `AppMotion` (bei reduzierter Bewegung `Duration.zero`); `AppTypography` und `AppRadius` direkt nur in selbst gezeichneten Elementen (Navigationsleiste, Logo, `IconTile`, Karten mit `cardShadow`). Komponentenmaße bleiben lokal im Widget.
- Insets und Ausrichtung direktional (`EdgeInsetsDirectional`, `AlignmentDirectional`).
- Future-liefernde Callbacks an `VoidCallback` explizit mit `unawaited(...)` – die Lints `discarded_futures`/`unawaited_futures` erkennen Arrow-Callbacks nicht. Fehler daraus landen über `PlatformDispatcher.onError` im Talker-Log (`main.dart`).
- Eigene `Semantics(excludeSemantics: true)` um Tap-Ziele brauchen `onTap`, sonst können Screenreader sie nicht auslösen.

<!-- TODO: Eigene Konventionen ergänzen (State-Management, Testing-Setup, ...) -->
