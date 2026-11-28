import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_spacing.dart';
import '../../shopping_list/providers/pending_shopping_list_entries_provider.dart';
import '../../shopping_list/providers/shopping_list_overview_provider.dart';
import '../../shopping_list/widgets/shopping_list_all_checked_banner.dart';
import '../../shopping_list/widgets/shopping_list_checked_section.dart';
import '../../shopping_list/widgets/shopping_list_empty_state.dart';
import '../../shopping_list/widgets/shopping_list_entry_field.dart';
import '../../shopping_list/widgets/shopping_list_group_card.dart';
import '../../shopping_list/widgets/shopping_list_pending_entries.dart';
import '../../shopping_list/widgets/shopping_list_progress.dart';

@RoutePage()
class ShoppingListPage extends ConsumerWidget {
  const ShoppingListPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final overview = ref.watch(shoppingListOverviewProvider);
    if (overview == null) {
      return const Center(child: CircularProgressIndicator());
    }
    final pendingEntries = ref.watch(pendingShoppingListEntriesProvider);
    return Column(
      children: [
        if (overview.totalCount > 0)
          ShoppingListProgress(
            checkedCount: overview.checkedCount,
            totalCount: overview.totalCount,
          ),
        ShoppingListEntryField(listId: overview.listId),
        Expanded(
          child: overview.totalCount == 0 && pendingEntries.isEmpty
              ? const ShoppingListEmptyState()
              : ListView(
                  keyboardDismissBehavior:
                      ScrollViewKeyboardDismissBehavior.onDrag,
                  padding: EdgeInsetsDirectional.only(
                    bottom:
                        MediaQuery.paddingOf(context).bottom +
                        AppSpacing.space16,
                  ),
                  children: [
                    if (pendingEntries.isNotEmpty)
                      ShoppingListPendingEntries(texts: pendingEntries),
                    if (overview.groups.isEmpty && overview.totalCount > 0)
                      const ShoppingListAllCheckedBanner(),
                    for (final group in overview.groups)
                      ShoppingListGroupCard(group: group),
                    if (overview.checkedItems.isNotEmpty)
                      ShoppingListCheckedSection(items: overview.checkedItems),
                  ],
                ),
        ),
      ],
    );
  }
}
