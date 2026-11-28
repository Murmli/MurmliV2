import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../shopping_list/providers/pending_shopping_list_entries_provider.dart';
import '../../shopping_list/providers/shopping_list_overview_provider.dart';
import '../../shopping_list/widgets/shopping_list_all_checked_banner.dart';
import '../../shopping_list/widgets/shopping_list_checked_section_sliver.dart';
import '../../shopping_list/widgets/shopping_list_empty_state.dart';
import '../../shopping_list/widgets/shopping_list_entry_field.dart';
import '../../shopping_list/widgets/shopping_list_group_sliver.dart';
import '../../shopping_list/widgets/shopping_list_pending_entries.dart';
import '../../shopping_list/widgets/shopping_list_progress.dart';
import '../home_tab.dart';
import '../widgets/home_header.dart';

@RoutePage()
class ShoppingListPage extends ConsumerWidget {
  const ShoppingListPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final title = HomeTab.shoppingList.title(context.t);
    final overview = ref.watch(shoppingListOverviewProvider);
    if (overview == null) {
      return Column(
        children: [
          HomeHeader(title: title),
          const Expanded(child: Center(child: CircularProgressIndicator())),
        ],
      );
    }
    final pendingEntries = ref.watch(pendingShoppingListEntriesProvider);
    final background = context.colors.background;
    return CustomScrollView(
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      slivers: [
        SliverFloatingHeader(
          child: Material(
            color: background,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                HomeHeader(title: title),
                if (overview.totalCount > 0)
                  ShoppingListProgress(
                    checkedCount: overview.checkedCount,
                    totalCount: overview.totalCount,
                  ),
              ],
            ),
          ),
        ),
        PinnedHeaderSliver(
          child: Material(
            color: background,
            child: ShoppingListEntryField(listId: overview.listId),
          ),
        ),
        if (overview.totalCount == 0 && pendingEntries.isEmpty)
          const SliverFillRemaining(
            hasScrollBody: false,
            child: ShoppingListEmptyState(),
          )
        else ...[
          if (pendingEntries.isNotEmpty)
            SliverToBoxAdapter(
              child: ShoppingListPendingEntries(texts: pendingEntries),
            ),
          if (overview.groups.isEmpty && overview.totalCount > 0)
            const SliverToBoxAdapter(child: ShoppingListAllCheckedBanner()),
          for (final group in overview.groups)
            ShoppingListGroupSliver(group: group),
          if (overview.checkedItems.isNotEmpty)
            ShoppingListCheckedSectionSliver(items: overview.checkedItems),
        ],
        SliverPadding(
          padding: EdgeInsetsDirectional.only(
            bottom: MediaQuery.paddingOf(context).bottom + AppSpacing.space16,
          ),
        ),
      ],
    );
  }
}
