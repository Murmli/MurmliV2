import 'package:flutter/widgets.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'app_foreground_provider.g.dart';

/// Whether the app is in the foreground: resumed or inactive (control
/// center, incoming call), or not yet known.
@Riverpod(keepAlive: true)
class AppForeground extends _$AppForeground {
  @override
  bool build() {
    final listener = AppLifecycleListener(
      onStateChange: (lifecycleState) => state = _isForeground(lifecycleState),
    );
    ref.onDispose(listener.dispose);
    return _isForeground(WidgetsBinding.instance.lifecycleState);
  }
}

bool _isForeground(AppLifecycleState? lifecycleState) =>
    switch (lifecycleState) {
      AppLifecycleState.hidden ||
      AppLifecycleState.paused ||
      AppLifecycleState.detached => false,
      AppLifecycleState.resumed || AppLifecycleState.inactive || null => true,
    };
