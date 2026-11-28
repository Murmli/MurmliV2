import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../models/shopping_list_item.dart';

part 'leaving_shopping_list_items_provider.g.dart';

@Riverpod(keepAlive: true)
class LeavingShoppingListItems extends _$LeavingShoppingListItems {
  @override
  Map<String, ShoppingListItem> build() => const {};

  void add(ShoppingListItem item) => state = {...state, item.id: item};

  void remove(String itemId) => state = {...state}..remove(itemId);

  void removeChecked() =>
      state = {...state}..removeWhere((_, item) => item.checked);
}
