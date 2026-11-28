import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'recently_toggled_item_provider.g.dart';

@Riverpod(keepAlive: true)
class RecentlyToggledItem extends _$RecentlyToggledItem {
  @override
  String? build() => null;

  void mark(String itemId) => state = itemId;

  void clear() => state = null;
}
