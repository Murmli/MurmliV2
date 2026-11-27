import 'dart:collection';

import 'package:flutter/services.dart';

const storageErrorCode = 'storage-failure';

/// Secure storage entries whose deletion always fails, clearing and writing
/// fail on request.
class FailingSecureStorage extends MapBase<String, String> {
  FailingSecureStorage(
    Map<String, String> entries, {
    this.failsToClear = false,
    this.failsToWrite = false,
  }) : _entries = {...entries};

  final Map<String, String> _entries;
  final bool failsToClear;
  final bool failsToWrite;

  @override
  String? operator [](Object? key) => _entries[key];

  @override
  void operator []=(String key, String value) {
    if (failsToWrite) {
      throw PlatformException(code: storageErrorCode);
    }
    _entries[key] = value;
  }

  @override
  Iterable<String> get keys => _entries.keys;

  @override
  String? remove(Object? key) =>
      throw PlatformException(code: storageErrorCode);

  @override
  void clear() {
    if (failsToClear) {
      throw PlatformException(code: storageErrorCode);
    }
    _entries.clear();
  }
}
