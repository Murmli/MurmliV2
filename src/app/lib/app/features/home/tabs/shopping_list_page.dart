import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/network/network_available_provider.dart';
import '../../../core/sync/sync_core.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/round_icon_button.dart';
import '../../shopping_list/providers/pending_shopping_list_entries_provider.dart';
import '../../shopping_list/providers/shopping_list_overview_provider.dart';
import '../../shopping_list/shopping_list_name.dart';
import '../../shopping_list/widgets/shopping_list_all_checked_banner.dart';
import '../../shopping_list/widgets/shopping_list_checked_section_sliver.dart';
import '../../shopping_list/widgets/shopping_list_empty_state.dart';
import '../../shopping_list/widgets/shopping_list_entry_field.dart';
import '../../shopping_list/widgets/shopping_list_group_sliver.dart';
import '../../shopping_list/widgets/shopping_list_menu.dart';
import '../../shopping_list/widgets/shopping_list_pending_entries.dart';
import '../home_tab.dart';
import '../widgets/home_header.dart';

@RoutePage()
class ShoppingListPage extends ConsumerWidget {
  const ShoppingListPage({super.key});

  static const _headerPadding = EdgeInsetsDirectional.fromSTEB(
    AppSpacing.screenMargin,
    28,
    AppSpacing.screenMargin,
    AppSpacing.space12,
  );

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final overview = ref.watch(shoppingListOverviewProvider);
    final firstSyncCompleted = ref.watch(firstSyncCompletedProvider).value;
    if (overview == null || firstSyncCompleted != true) {
      return Column(
        children: [
          HomeHeader(
            title: HomeTab.shoppingList.title(context.t),
            padding: _headerPadding,
          ),
          Expanded(
            child: ShoppingListLoadingState(
              showsOfflineHint:
                  firstSyncCompleted != true &&
                  !ref.watch(networkAvailableProvider),
            ),
          ),
        ],
      );
    }
    final title = shoppingListDisplayName(overview.listName, context.t);
    final pendingEntries = ref.watch(pendingShoppingListEntriesProvider);
    final colors = context.colors;
    final hasItems = overview.totalCount > 0;
    return CustomScrollView(
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      slivers: [
        SliverFloatingHeader(
          child: Material(
            color: colors.background,
            child: HomeHeader(
              title: title,
              padding: _headerPadding,
              trailing: RoundIconButton(
                icon: Icons.more_horiz,
                tooltip: context.t.shoppingList.menu.tooltip,
                iconSize: 22,
                onPressed: () => unawaited(showShoppingListMenu(context, ref)),
              ),
            ),
          ),
        ),
        PinnedHeaderSliver(
          child: Material(
            color: colors.background,
            child: ShoppingListEntryField(
              listId: overview.listId,
              checkedCount: overview.checkedCount,
              totalCount: overview.totalCount,
            ),
          ),
        ),
        if (!hasItems && pendingEntries.isEmpty)
          const SliverFillRemaining(
            hasScrollBody: false,
            child: ShoppingListEmptyState(),
          )
        else ...[
          if (pendingEntries.isNotEmpty)
            SliverToBoxAdapter(
              child: ShoppingListPendingEntries(texts: pendingEntries),
            ),
          if (overview.checkedCount == overview.totalCount && hasItems)
            const SliverToBoxAdapter(child: ShoppingListAllCheckedBanner()),
          for (final group in overview.groups)
            ShoppingListGroupSliver(
              key: ValueKey(group.category),
              group: group,
            ),
          if (overview.checkedItems.isNotEmpty)
            ShoppingListCheckedSectionSliver(
              items: overview.checkedItems,
              count: overview.checkedCount,
              leavingItemIds: overview.leavingCheckedItemIds,
            ),
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
