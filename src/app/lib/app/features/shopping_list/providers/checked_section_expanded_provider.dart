import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/storage/preference_keys.dart';
import '../../../core/storage/shared_preferences_provider.dart';
import 'leaving_shopping_list_items_provider.dart';

part 'checked_section_expanded_provider.g.dart';

@Riverpod(keepAlive: true)
class CheckedSectionExpanded extends _$CheckedSectionExpanded {
  @override
  bool build() =>
      ref
          .watch(sharedPreferencesProvider)
          .getBool(PreferenceKeys.shoppingListCheckedExpanded) ??
      true;

  Future<void> toggle() async {
    final isExpanded = !state;
    await ref
        .read(sharedPreferencesProvider)
        .setBool(PreferenceKeys.shoppingListCheckedExpanded, isExpanded);
    state = isExpanded;
    // Leaving checked rows are no longer built and would never finish leaving.
    if (!isExpanded) {
      ref.read(leavingShoppingListItemsProvider.notifier).removeChecked();
    }
  }
}
