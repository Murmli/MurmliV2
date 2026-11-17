import 'package:flutter/foundation.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'app_endpoints.dart';
import 'flavor_provider.dart';

part 'app_endpoints_provider.g.dart';

@Riverpod(keepAlive: true)
AppEndpoints appEndpoints(Ref ref) =>
    AppEndpoints.of(ref.watch(flavorProvider), defaultTargetPlatform);
