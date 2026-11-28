import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';
import '../../core/widgets/confirmation_dialog.dart';
import '../../core/widgets/round_icon_button.dart';
import 'models/market_category.dart';
import 'providers/shopping_list_overview_provider.dart';
import 'providers/shopping_list_repository.dart';
import 'widgets/market_category_tile.dart';

@RoutePage()
class CategoryOrderPage extends HookConsumerWidget {
  const CategoryOrderPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final translations = context.t.shoppingList;
    final storedOrder = ref.watch(marketCategoryOrderProvider).value;
    final localOrder = useState<List<MarketCategory>?>(null);
    final order = localOrder.value ?? storedOrder;

    void move(int oldIndex, int newIndex) {
      final newOrder = [...order!];
      newOrder.insert(newIndex, newOrder.removeAt(oldIndex));
      localOrder.value = newOrder;
      unawaited(
        ref.read(shoppingListRepositoryProvider).setCategoryOrder(newOrder),
      );
    }

    Future<void> reset() async {
      final dialogTranslations = translations.categoryOrder.resetDialog;
      final isConfirmed = await showConfirmationDialog(
        context,
        icon: Icons.restart_alt,
        title: dialogTranslations.title,
        message: dialogTranslations.message,
        cancelLabel: dialogTranslations.cancel,
        confirmLabel: dialogTranslations.confirm,
      );
      if (!isConfirmed || !context.mounted) {
        return;
      }
      localOrder.value = MarketCategory.values;
      await ref.read(shoppingListRepositoryProvider).resetCategoryOrder();
    }

    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsetsDirectional.only(
                top: AppSpacing.space24,
                start: AppSpacing.space16,
                end: AppSpacing.space16,
              ),
              child: Row(
                spacing: AppSpacing.space12,
                children: [
                  RoundIconButton(
                    icon: Icons.arrow_back,
                    tooltip: MaterialLocalizations.of(context)
                        .backButtonTooltip,
                    onPressed: () =>
                        unawaited(Navigator.of(context).maybePop()),
                  ),
                  Expanded(
                    child: Semantics(
                      header: true,
                      child: Text(
                        translations.menu.sortCategories,
                        style: textTheme.headlineSmall,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            Expanded(
              child: order == null
                  ? const Center(child: CircularProgressIndicator())
                  : CustomScrollView(
                      slivers: [
                        SliverPadding(
                          padding: const EdgeInsetsDirectional.symmetric(
                            horizontal: AppSpacing.screenMargin,
                            vertical: 14,
                          ),
                          sliver: SliverToBoxAdapter(
                            child: Text(
                              translations.categoryOrder.intro,
                              style: textTheme.bodyMedium!.copyWith(
                                fontSize: 14,
                                height: 20 / 14,
                                color: colors.inkSecondary,
                              ),
                            ),
                          ),
                        ),
                        SliverPadding(
                          padding: const EdgeInsetsDirectional.only(
                            start: AppSpacing.screenMargin,
                            end: AppSpacing.screenMargin,
                            bottom: AppSpacing.space24,
                          ),
                          sliver: DecoratedSliver(
                            decoration: BoxDecoration(
                              color: colors.surface,
                              borderRadius: BorderRadius.circular(
                                AppRadius.card,
                              ),
                              boxShadow: colors.cardShadow,
                            ),
                            sliver: SliverPadding(
                              padding: const EdgeInsetsDirectional.only(
                                start: AppSpacing.space12,
                                end: AppSpacing.space8,
                              ),
                              sliver: SliverReorderableList(
                                itemCount: order.length,
                                onReorderItem: move,
                                proxyDecorator: (child, index, animation) =>
                                    _DraggedCategoryRow(child: child),
                                itemBuilder: (context, index) => _CategoryRow(
                                  key: ValueKey(order[index]),
                                  category: order[index],
                                  index: index,
                                  onMoveUp: index == 0
                                      ? null
                                      : () => move(index, index - 1),
                                  onMoveDown: index == order.length - 1
                                      ? null
                                      : () => move(index, index + 1),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
            ),
          ],
        ),
      ),
      bottomNavigationBar: DecoratedBox(
        decoration: BoxDecoration(
          color: colors.background,
          border: Border(top: BorderSide(color: colors.line)),
        ),
        child: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsetsDirectional.symmetric(
              horizontal: AppSpacing.screenMargin,
              vertical: 14,
            ),
            child: Center(
              heightFactor: 1,
              child: TextButton.icon(
                onPressed:
                    order == null || listEquals(order, MarketCategory.values)
                    ? null
                    : () => unawaited(reset()),
                icon: const Icon(Icons.restart_alt),
                label: Text(translations.categoryOrder.restoreDefault),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _CategoryRow extends StatelessWidget {
  const _CategoryRow({
    required this.category,
    required this.index,
    required this.onMoveUp,
    required this.onMoveDown,
    super.key,
  });

  final MarketCategory category;
  final int index;
  final VoidCallback? onMoveUp;
  final VoidCallback? onMoveDown;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final translations = context.t;
    final label = category.label(translations);
    final orderTranslations = translations.shoppingList.categoryOrder;
    final buttonConstraints = BoxConstraints.tight(const Size.square(44));
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (index > 0) const Divider(),
        ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 52),
          child: Row(
            spacing: 10,
            children: [
              ReorderableDragStartListener(
                index: index,
                child: ExcludeSemantics(
                  child: SizedBox(
                    width: 20,
                    height: 52,
                    child: Icon(
                      Icons.drag_indicator,
                      size: 18,
                      color: colors.inkTertiary,
                    ),
                  ),
                ),
              ),
              MarketCategoryTile(category: category, size: 32),
              Expanded(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextTheme.of(context).bodyMedium!.copyWith(
                    fontSize: 14,
                    height: 20 / 14,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              IconButton(
                tooltip: orderTranslations.moveUp(name: label),
                constraints: buttonConstraints,
                color: colors.inkSecondary,
                icon: const Icon(Icons.keyboard_arrow_up),
                onPressed: onMoveUp,
              ),
              IconButton(
                tooltip: orderTranslations.moveDown(name: label),
                constraints: buttonConstraints,
                color: colors.inkSecondary,
                icon: const Icon(Icons.keyboard_arrow_down),
                onPressed: onMoveDown,
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _DraggedCategoryRow extends StatelessWidget {
  const _DraggedCategoryRow({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(16),
        boxShadow: colors.navigationShadow,
      ),
      child: Material(type: MaterialType.transparency, child: child),
    );
  }
}
