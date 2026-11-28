import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/services.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'network_available_provider.g.dart';

/// Whether the system reports a network, `true` until it reports otherwise.
/// Only a hint: a network may lack internet access, so whether requests
/// succeed stays authoritative.
@Riverpod(keepAlive: true)
class NetworkAvailable extends _$NetworkAvailable {
  var _changeReported = false;

  @override
  bool build() {
    final connectivity = Connectivity();
    final subscription = connectivity.onConnectivityChanged.listen((results) {
      _changeReported = true;
      state = results.hasConnectivity;
    });
    ref.onDispose(subscription.cancel);
    unawaited(_readInitialState(connectivity));
    return true;
  }

  // Android reports no change at the start while offline.
  Future<void> _readInitialState(Connectivity connectivity) async {
    final List<ConnectivityResult> results;
    try {
      results = await connectivity.checkConnectivity();
    } on PlatformException {
      return;
    }
    if (!_changeReported && ref.mounted) {
      state = results.hasConnectivity;
    }
  }
}
