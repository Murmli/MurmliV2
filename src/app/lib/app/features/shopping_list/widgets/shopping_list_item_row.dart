import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../models/recipe_share.dart';
import '../models/shopping_list_item.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';

class ShoppingListItemRow extends StatelessWidget {
  const ShoppingListItemRow({required this.item, super.key});

  final ShoppingListItem item;

  @override
  Widget build(BuildContext context) {
    final textTheme = TextTheme.of(context);
    final quantity = quantityLabel(
      item,
      context.t,
      TranslationProvider.of(context).locale.languageCode,
    );
    return MergeSemantics(
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: Row(
          spacing: 10,
          children: [
            SizedBox.square(
              dimension: 44,
              child: Center(child: MarketCategoryTile(category: item.category)),
            ),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    item.name,
                    style: textTheme.bodyMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  if (item.recipeShares.isNotEmpty)
                    _RecipeLine(recipeShares: item.recipeShares),
                ],
              ),
            ),
            if (quantity.isNotEmpty)
              Text(
                quantity,
                style: textTheme.labelLarge?.copyWith(
                  color: context.colors.inkSecondary,
                ),
              ),
          ],
        ),
      ),
    );
  }
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
