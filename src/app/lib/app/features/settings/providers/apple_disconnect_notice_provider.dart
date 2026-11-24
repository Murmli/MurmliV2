import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'apple_disconnect_notice_provider.g.dart';

@Riverpod(keepAlive: true)
class PendingAppleDisconnectNotice extends _$PendingAppleDisconnectNotice {
  @override
  bool build() => false;

  void raise() => state = true;

  void dismiss() => state = false;
}
