import 'package:flutter/foundation.dart';

import 'flavor.dart';

class AppEndpoints {
  const AppEndpoints._({required this.api, required this.auth});

  factory AppEndpoints.of(Flavor flavor, TargetPlatform platform) {
    // The Android emulator reaches the host's loopback only via 10.0.2.2.
    final host = platform == TargetPlatform.android ? '10.0.2.2' : 'localhost';
    return switch (flavor) {
      Flavor.local => AppEndpoints._(
        api: Uri(scheme: 'http', host: host, port: 5020),
        auth: Uri(scheme: 'http', host: host, port: 5021),
      ),
      Flavor.development => AppEndpoints._(
        api: Uri.https(
          'api.kinddune-9cab4a57.francecentral.azurecontainerapps.io',
        ),
        auth: Uri.https(
          'auth.kinddune-9cab4a57.francecentral.azurecontainerapps.io',
        ),
      ),
      Flavor.production => throw UnimplementedError(
        'The production flavor has no endpoints until the release.',
      ),
    };
  }

  final Uri api;
  final Uri auth;
}
