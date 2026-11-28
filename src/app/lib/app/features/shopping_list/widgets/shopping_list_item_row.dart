import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/recipe_share.dart';
import '../models/shopping_list_item.dart';
import '../providers/checked_section_expanded_provider.dart';
import '../providers/leaving_shopping_list_items_provider.dart';
import '../providers/recently_toggled_item_provider.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';
import 'recipe_dot.dart';
import 'shopping_list_item_editor.dart';
import 'shopping_list_item_menu.dart';

class ShoppingListItemRow extends HookConsumerWidget {
  ShoppingListItemRow({
    required this.item,
    required this.showDivider,
    required this.isLeaving,
    required this.animatesWithSection,
  }) : super(key: ValueKey((item.id, isLeaving)));

  final ShoppingListItem item;
  final bool showDivider;
  final bool isLeaving;

  /// Whether the surrounding section collapses or expands in place of this
  /// row's own appearing and leaving; the row then only keeps the timing.
  final bool animatesWithSection;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final quantity = quantityLabel(
      item,
      context.t,
      TranslationProvider.of(context).locale.languageCode,
    );
    final duration = AppMotion.short(context);
    final appears = useMemoized(
      () => !isLeaving && ref.read(recentlyToggledItemProvider) == item.id,
    );
    final visibility = useAnimationController(
      duration: duration,
      initialValue: appears ? 0 : 1,
    );
    final curvedVisibility = useMemoized(
      () => CurvedAnimation(
        parent: visibility,
        curve: Curves.easeOut,
        reverseCurve: Curves.easeIn,
      ),
      [visibility],
    );
    useEffect(() => curvedVisibility.dispose, [curvedVisibility]);
    useEffect(() {
      if (isLeaving) {
        final leavingItems = ref.read(
          leavingShoppingListItemsProvider.notifier,
        );
        unawaited(
          visibility.reverse().then((_) => leavingItems.remove(item.id)),
        );
      } else if (appears) {
        // Providers must not change while widgets build.
        unawaited(
          Future.microtask(
            ref.read(recentlyToggledItemProvider.notifier).clear,
          ),
        );
        unawaited(visibility.forward());
      }
      return null;
    }, const []);

    void toggleChecked() {
      unawaited(HapticFeedback.selectionClick());
      if (duration > Duration.zero) {
        ref.read(leavingShoppingListItemsProvider.notifier).add(item);
        // A collapsed checked section would replay the appearance on expanding.
        if (item.checked || ref.read(checkedSectionExpandedProvider)) {
          ref.read(recentlyToggledItemProvider.notifier).mark(item.id);
        }
      }
      unawaited(
        ref
            .read(shoppingListRepositoryProvider)
            .setChecked(item.id, checked: !item.checked),
      );
    }

    final rowVisibility = animatesWithSection
        ? kAlwaysCompleteAnimation
        : curvedVisibility;
    final row = SizeTransition(
      sizeFactor: rowVisibility,
      alignment: AlignmentDirectional.topCenter,
      child: FadeTransition(
        opacity: rowVisibility,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (showDivider) const Divider(),
            Dismissible(
              key: ValueKey('dismiss-${item.id}'),
              direction: DismissDirection.endToStart,
              background: const _DeleteBackground(),
              confirmDismiss: (_) async {
                await ref
                    .read(shoppingListRepositoryProvider)
                    .deleteItem(item.id);
                return true;
              },
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 48),
                child: IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    spacing: 6,
                    children: [
                      Semantics(
                        container: true,
                        button: true,
                        excludeSemantics: true,
                        label: item.checked
                            ? context.t.shoppingList.uncheckItem(
                                name: item.name,
                              )
                            : context.t.shoppingList.checkItem(name: item.name),
                        onTap: toggleChecked,
                        child: SizedBox(
                          width: 48,
                          child: Material(
                            type: MaterialType.transparency,
                            child: InkWell(
                              customBorder: const CircleBorder(),
                              onTap: toggleChecked,
                              // The design centers the tile in a 44 wide slot;
                              // the tap area keeps 48 and extends past it.
                              child: Align(
                                alignment: AlignmentDirectional.centerStart,
                                child: Padding(
                                  padding: const EdgeInsetsDirectional.only(
                                    start: 4,
                                  ),
                                  child: item.checked
                                      ? const _CheckedTile()
                                      : MarketCategoryTile(
                                          category: item.category,
                                        ),
                                ),
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
                            onLongPressHint:
                                context.t.shoppingList.item.openDetails,
                            child: Material(
                              type: MaterialType.transparency,
                              child: InkWell(
                                onTap: () => unawaited(
                                  showShoppingListItemMenu(context, item),
                                ),
                                onLongPress: () => unawaited(
                                  showShoppingListItemEditor(context, item),
                                ),
                                child: Row(
                                  spacing: 10,
                                  children: [
                                    Expanded(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        mainAxisSize: MainAxisSize.min,
                                        spacing: 1,
                                        children: [
                                          Text(
                                            item.name,
                                            style: textTheme.bodyMedium
                                                ?.copyWith(
                                                  fontWeight: FontWeight.w600,
                                                  height: 20 / 15,
                                                  color: item.checked
                                                      ? colors.inkTertiary
                                                      : null,
                                                  decoration: item.checked
                                                      ? TextDecoration
                                                            .lineThrough
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
            ),
          ],
        ),
      ),
    );
    return isLeaving ? IgnorePointer(child: ExcludeSemantics(child: row)) : row;
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
      borderRadius: BorderRadius.circular((_size * 0.32).roundToDouble()),
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
          child: hasSingleRecipe ? const RecipeDot() : const _RecipeDotPair(),
        ),
        Flexible(
          child: Text(
            hasSingleRecipe
                ? recipeShares.single.recipeTitle
                : context.t.shoppingList.recipeCount(
                    count: recipeShares.length,
                  ),
            style: TextTheme.of(context).labelSmall?.copyWith(
              fontWeight: FontWeight.w700,
              height: 14 / 11,
              color: context.colors.primaryText,
            ),
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
        const PositionedDirectional(start: 0, child: RecipeDot()),
        PositionedDirectional(
          start: 4,
          child: RecipeDot(border: context.colors.surface),
        ),
      ],
    ),
  );
}
