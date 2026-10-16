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

<!-- TODO: Eigene Konventionen ergänzen (State-Management, Testing-Setup, ...) -->
