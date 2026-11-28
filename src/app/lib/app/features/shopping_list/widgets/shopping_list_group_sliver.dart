import 'dart:math' as math;

import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_group.dart';
import 'shopping_list_item_row.dart';
import 'shopping_list_pinned_header.dart';
import 'shopping_list_section.dart';

class ShoppingListGroupSliver extends StatelessWidget {
  const ShoppingListGroupSliver({required this.group, super.key});

  final ShoppingListGroup group;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final titleStyle = ShoppingListPinnedHeader.titleStyleOf(context);
    final countStyle = TextTheme.of(context).labelSmall!
        .copyWith(fontSize: 12, height: 16 / 12, color: colors.inkTertiary);
    return ShoppingListSection(
      items: group.items,
      leavingItemIds: group.leavingItemIds,
      headerContentHeight: math.max(
        ShoppingListPinnedHeader.lineHeightOf(context, titleStyle),
        ShoppingListPinnedHeader.lineHeightOf(context, countStyle),
      ),
      header: Padding(
        padding: const EdgeInsetsDirectional.symmetric(
          horizontal: AppSpacing.space4,
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          children: [
            Expanded(
              child: Semantics(
                header: true,
                child: Text(group.category.label(context.t), style: titleStyle),
              ),
            ),
            Text('${group.count}', style: countStyle),
          ],
        ),
      ),
      body: Container(
        padding: const EdgeInsetsDirectional.only(
          start: 6,
          end: AppSpacing.space16,
        ),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppRadius.listGroup),
          boxShadow: colors.cardShadow,
        ),
        child: Column(
          children: [
            for (final (index, item) in group.items.indexed)
              ShoppingListItemRow(
                item: item,
                showDivider: index > 0,
                isLeaving: group.leavingItemIds.contains(item.id),
                animatesWithSection: group.items.length == 1,
              ),
          ],
        ),
      ),
    );
  }
}
