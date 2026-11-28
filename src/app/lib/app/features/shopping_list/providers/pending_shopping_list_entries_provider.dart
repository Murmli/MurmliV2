import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/authentication/signed_in_account_provider.dart';
import 'shopping_item_parser.dart';
import 'shopping_list_repository.dart';

part 'pending_shopping_list_entries_provider.g.dart';

@Riverpod(keepAlive: true)
class PendingShoppingListEntries extends _$PendingShoppingListEntries {
  @override
  List<String> build() => const [];

  Future<void> submit({required String listId, required String text}) async {
    final trimmedText = text.trim();
    if (trimmedText.isEmpty) {
      return;
    }
    final accountId = ref.read(signedInAccountProvider)?.id;
    state = [...state, trimmedText];
    try {
      final items = await ref
          .read(shoppingItemParserProvider)
          .parse(trimmedText);
      if (ref.read(signedInAccountProvider)?.id != accountId) {
        return;
      }
      final repository = ref.read(shoppingListRepositoryProvider);
      for (final item in items) {
        await repository.addItem(
          listId: listId,
          name: item.name,
          category: item.category,
          quantity: item.quantity,
          unit: item.unit,
        );
      }
    } finally {
      final index = state.indexOf(trimmedText);
      state = [...state.take(index), ...state.skip(index + 1)];
    }
  }
}
