import 'dart:async';
import 'dart:math' as math;

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_item.dart';
import '../providers/checked_section_expanded_provider.dart';
import 'shopping_list_item_row.dart';
import 'shopping_list_pinned_header.dart';
import 'shopping_list_section.dart';

class ShoppingListCheckedSectionSliver extends ConsumerWidget {
  const ShoppingListCheckedSectionSliver({
    required this.items,
    required this.count,
    required this.leavingItemIds,
    super.key,
  });

  static const _minimumHeaderHeight = 40.0;

  final List<ShoppingListItem> items;
  final int count;
  final Set<String> leavingItemIds;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final isExpanded = ref.watch(checkedSectionExpandedProvider);
    final titleStyle = TextTheme.of(context).titleSmall!
        .copyWith(color: colors.inkSecondary);
    return ShoppingListSection(
      items: items,
      leavingItemIds: leavingItemIds,
      headerContentHeight: math.max(
        _minimumHeaderHeight,
        ShoppingListPinnedHeader.lineHeightOf(context, titleStyle),
      ),
      header: Semantics(
        container: true,
        button: true,
        header: true,
        expanded: isExpanded,
        child: InkWell(
          onTap: () => unawaited(
            ref.read(checkedSectionExpandedProvider.notifier).toggle(),
          ),
          child: Row(
            spacing: 6,
            children: [
              AnimatedRotation(
                turns: isExpanded ? 0 : -0.25,
                duration: AppMotion.short(context),
                child: Icon(
                  Icons.expand_more,
                  size: 16,
                  color: colors.inkSecondary,
                ),
              ),
              Text(
                context.t.shoppingList.checkedSection(count: count),
                style: titleStyle,
              ),
            ],
          ),
        ),
      ),
      body: isExpanded
          ? Container(
              padding: const EdgeInsetsDirectional.only(
                start: 4,
                end: AppSpacing.space16,
              ),
              decoration: BoxDecoration(
                color: colors.surfaceSecondary,
                borderRadius: BorderRadius.circular(AppRadius.listGroup),
              ),
              child: Column(
                children: [
                  for (final (index, item) in items.indexed)
                    ShoppingListItemRow(
                      item: item,
                      showDivider: index > 0,
                      isLeaving: leavingItemIds.contains(item.id),
                    ),
                ],
              ),
            )
          : null,
    );
  }
}
