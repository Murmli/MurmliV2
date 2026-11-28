import 'package:flutter/foundation.dart';

const _minimumMaxRequestBytes = 65536;

/// The settings of the server, read by hand instead of with dart_mappable:
/// each value is optional and read on its own. A value is `null` when it is
/// missing, not an integer or implausible, so the device keeps its previous
/// value.
@immutable
class SyncServerSettings {
  factory SyncServerSettings.read(Object? json) {
    final fields = json is Map<String, Object?>
        ? json
        : const <String, Object?>{};
    int? plausible(String key, int minimum) => switch (fields[key]) {
      final int value when value >= minimum => value,
      _ => null,
    };
    return SyncServerSettings._(
      pollIntervalSeconds: plausible('pollIntervalSeconds', 1),
      maxCommandsPerRequest: plausible('maxCommandsPerRequest', 1),
      maxRequestBytes: plausible('maxRequestBytes', _minimumMaxRequestBytes),
    );
  }

  const SyncServerSettings._({
    required this.pollIntervalSeconds,
    required this.maxCommandsPerRequest,
    required this.maxRequestBytes,
  });

  final int? pollIntervalSeconds;
  final int? maxCommandsPerRequest;
  final int? maxRequestBytes;
}
