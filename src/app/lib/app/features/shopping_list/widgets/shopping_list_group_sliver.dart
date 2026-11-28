import 'dart:math' as math;

import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_group.dart';
import 'shopping_list_item_row.dart';
import 'shopping_list_pinned_header.dart';

class ShoppingListGroupSliver extends StatelessWidget {
  const ShoppingListGroupSliver({required this.group, super.key});

  final ShoppingListGroup group;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final titleStyle = textTheme.titleSmall!.copyWith(
      color: colors.inkSecondary,
    );
    final countStyle = textTheme.labelSmall!.copyWith(
      color: colors.inkTertiary,
    );
    return SliverMainAxisGroup(
      slivers: [
        ShoppingListPinnedHeader(
          contentHeight: math.max(
            ShoppingListPinnedHeader.lineHeightOf(context, titleStyle),
            ShoppingListPinnedHeader.lineHeightOf(context, countStyle),
          ),
          child: Padding(
            padding: const EdgeInsetsDirectional.symmetric(
              horizontal: AppSpacing.space4,
            ),
            child: Row(
              children: [
                Expanded(
                  child: Semantics(
                    header: true,
                    child: Text(
                      group.category.label(context.t),
                      style: titleStyle,
                    ),
                  ),
                ),
                Text('${group.items.length}', style: countStyle),
              ],
            ),
          ),
        ),
        SliverPadding(
          padding: const EdgeInsetsDirectional.only(
            start: AppSpacing.screenMargin,
            end: AppSpacing.screenMargin,
            bottom: AppSpacing.space12,
          ),
          sliver: SliverToBoxAdapter(
            child: Container(
              padding: const EdgeInsetsDirectional.only(
                start: 4,
                end: AppSpacing.space16,
              ),
              decoration: BoxDecoration(
                color: colors.surface,
                borderRadius: BorderRadius.circular(AppRadius.listGroup),
                boxShadow: colors.cardShadow,
              ),
              child: Column(
                children: [
                  for (final (index, item) in group.items.indexed) ...[
                    if (index > 0) const Divider(),
                    ShoppingListItemRow(item: item, key: ValueKey(item.id)),
                  ],
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
