import 'dart:async';

import 'package:flutter/services.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/recipe_share.dart';
import '../models/shopping_list_item.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';
import 'shopping_list_item_menu.dart';

class ShoppingListItemRow extends ConsumerWidget {
  const ShoppingListItemRow({required this.item, super.key});

  final ShoppingListItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final quantity = quantityLabel(
      item,
      context.t,
      TranslationProvider.of(context).locale.languageCode,
    );
    void toggleChecked() {
      unawaited(HapticFeedback.selectionClick());
      unawaited(
        ref
            .read(shoppingListRepositoryProvider)
            .setChecked(item.id, checked: !item.checked),
      );
    }

    return Dismissible(
      key: ValueKey('dismiss-${item.id}'),
      direction: DismissDirection.endToStart,
      background: const _DeleteBackground(),
      confirmDismiss: (_) async {
        await ref.read(shoppingListRepositoryProvider).deleteItem(item.id);
        return true;
      },
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            spacing: 8,
            children: [
              Semantics(
                container: true,
                button: true,
                excludeSemantics: true,
                label: item.checked
                    ? context.t.shoppingList.uncheckItem(name: item.name)
                    : context.t.shoppingList.checkItem(name: item.name),
                onTap: toggleChecked,
                child: SizedBox(
                  width: 48,
                  child: Material(
                    type: MaterialType.transparency,
                    child: InkWell(
                      customBorder: const CircleBorder(),
                      onTap: toggleChecked,
                      child: Center(
                        child: item.checked
                            ? const _CheckedTile()
                            : MarketCategoryTile(category: item.category),
                      ),
                    ),
                  ),
                ),
              ),
              Expanded(
                child: MergeSemantics(
                  child: Semantics(
                    button: true,
                    onTapHint: context.t.shoppingList.item.openMenu,
                    child: Material(
                      type: MaterialType.transparency,
                      child: InkWell(
                        onTap: () => unawaited(
                          showShoppingListItemMenu(context, ref, item),
                        ),
                        child: Row(
                          spacing: 10,
                          children: [
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Text(
                                    item.name,
                                    style: textTheme.bodyMedium?.copyWith(
                                      fontWeight: FontWeight.w600,
                                      color: item.checked
                                          ? colors.inkTertiary
                                          : null,
                                      decoration: item.checked
                                          ? TextDecoration.lineThrough
                                          : null,
                                    ),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                  if (!item.checked &&
                                      item.recipeShares.isNotEmpty)
                                    _RecipeLine(
                                      recipeShares: item.recipeShares,
                                    ),
                                ],
                              ),
                            ),
                            if (quantity.isNotEmpty)
                              Text(
                                quantity,
                                style: textTheme.labelLarge?.copyWith(
                                  color: item.checked
                                      ? colors.inkTertiary
                                      : colors.inkSecondary,
                                ),
                              ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _DeleteBackground extends StatelessWidget {
  const _DeleteBackground();

  @override
  Widget build(BuildContext context) {
    final colorScheme = ColorScheme.of(context);
    return ColoredBox(
      color: colorScheme.errorContainer,
      child: Padding(
        padding: const EdgeInsetsDirectional.only(end: AppSpacing.space16),
        child: Align(
          alignment: AlignmentDirectional.centerEnd,
          child: Icon(
            Icons.delete_outline,
            color: colorScheme.onErrorContainer,
          ),
        ),
      ),
    );
  }
}

class _CheckedTile extends StatelessWidget {
  const _CheckedTile();

  static const _size = 36.0;

  @override
  Widget build(BuildContext context) => Container(
    width: _size,
    height: _size,
    alignment: Alignment.center,
    decoration: BoxDecoration(
      color: context.colors.onTarget,
      borderRadius: BorderRadius.circular(_size * 0.32),
    ),
    child: Icon(Icons.check_rounded, size: 20, color: context.colors.surface),
  );
}

class _RecipeLine extends StatelessWidget {
  const _RecipeLine({required this.recipeShares});

  final List<RecipeShare> recipeShares;

  @override
  Widget build(BuildContext context) {
    final hasSingleRecipe = recipeShares.length == 1;
    return Row(
      spacing: 6,
      children: [
        ExcludeSemantics(
          child: hasSingleRecipe ? const _RecipeDot() : const _RecipeDotPair(),
        ),
        Flexible(
          child: Text(
            hasSingleRecipe
                ? recipeShares.single.recipeTitle
                : context.t.shoppingList.recipeCount(
                    count: recipeShares.length,
                  ),
            style: TextTheme.of(context).labelSmall
                ?.copyWith(color: context.colors.primaryText),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}

class _RecipeDotPair extends StatelessWidget {
  const _RecipeDotPair();

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 10,
    height: 6,
    child: Stack(
      clipBehavior: Clip.none,
      children: [
        const PositionedDirectional(start: 0, child: _RecipeDot()),
        PositionedDirectional(
          start: 4,
          child: _RecipeDot(border: context.colors.surface),
        ),
      ],
    ),
  );
}

class _RecipeDot extends StatelessWidget {
  const _RecipeDot({this.border});

  final Color? border;

  @override
  Widget build(BuildContext context) => Container(
    width: 6,
    height: 6,
    decoration: BoxDecoration(
      color: context.colors.primary,
      shape: BoxShape.circle,
      border: switch (border) {
        final color? => Border.all(
          color: color,
          width: 1.5,
          strokeAlign: BorderSide.strokeAlignOutside,
        ),
        null => null,
      },
    ),
  );
}
