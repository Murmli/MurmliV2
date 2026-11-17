import 'package:flutter/services.dart' show appFlavor;
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'flavor.dart';

part 'flavor_provider.g.dart';

@Riverpod(keepAlive: true)
Flavor flavor(Ref ref) => Flavor.values.byName(
  appFlavor ??
      (throw StateError(
        'No app flavor is set. Start the app with flutter run or a flavor scheme, not the Xcode scheme Runner.',
      )),
);
