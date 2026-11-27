import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_item.dart';
import '../providers/checked_section_expanded_provider.dart';
import 'shopping_list_item_row.dart';

class ShoppingListCheckedSection extends ConsumerWidget {
  const ShoppingListCheckedSection({required this.items, super.key});

  final List<ShoppingListItem> items;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final isExpanded = ref.watch(checkedSectionExpandedProvider);
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
          Semantics(
            container: true,
            button: true,
            header: true,
            expanded: isExpanded,
            child: InkWell(
              onTap: () => unawaited(
                ref.read(checkedSectionExpandedProvider.notifier).toggle(),
              ),
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 40),
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
                      context.t.shoppingList.checkedSection(
                        count: items.length,
                      ),
                      style: TextTheme.of(context).titleSmall
                          ?.copyWith(color: colors.inkSecondary),
                    ),
                  ],
                ),
              ),
            ),
          ),
          if (isExpanded)
            Container(
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
                  for (final (index, item) in items.indexed) ...[
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
