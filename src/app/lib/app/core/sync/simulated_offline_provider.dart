import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'simulated_offline_provider.g.dart';

@Riverpod(keepAlive: true)
class SimulatedOffline extends _$SimulatedOffline {
  @override
  bool build() => false;

  void toggle() => state = !state;
}
