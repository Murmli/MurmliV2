import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_group.dart';
import 'shopping_list_item_row.dart';

class ShoppingListGroupCard extends StatelessWidget {
  const ShoppingListGroupCard({required this.group, super.key});

  final ShoppingListGroup group;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    return Padding(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: AppSpacing.space12,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        spacing: AppSpacing.space4,
        children: [
          Padding(
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
                      style: textTheme.titleSmall?.copyWith(
                        color: colors.inkSecondary,
                      ),
                    ),
                  ),
                ),
                Text(
                  '${group.items.length}',
                  style: textTheme.labelSmall?.copyWith(
                    color: colors.inkTertiary,
                  ),
                ),
              ],
            ),
          ),
          Container(
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
        ],
      ),
    );
  }
}
