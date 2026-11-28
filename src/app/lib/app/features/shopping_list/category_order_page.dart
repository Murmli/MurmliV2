import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/theme/app_spacing.dart';
import '../../core/widgets/confirmation_dialog.dart';
import 'models/market_category.dart';
import 'providers/shopping_list_overview_provider.dart';
import 'providers/shopping_list_repository.dart';
import 'widgets/market_category_tile.dart';

@RoutePage()
class CategoryOrderPage extends HookConsumerWidget {
  const CategoryOrderPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
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
      appBar: AppBar(
        title: Text(translations.menu.sortCategories),
        actions: [
          TextButton(
            onPressed: order == null || listEquals(order, MarketCategory.values)
                ? null
                : () => unawaited(reset()),
            child: Text(translations.categoryOrder.reset),
          ),
        ],
      ),
      body: order == null
          ? const Center(child: CircularProgressIndicator())
          : ReorderableListView.builder(
              buildDefaultDragHandles: false,
              itemCount: order.length,
              onReorderItem: move,
              itemBuilder: (context, index) => _CategoryTile(
                key: ValueKey(order[index]),
                category: order[index],
                index: index,
                onMoveUp: index == 0 ? null : () => move(index, index - 1),
                onMoveDown: index == order.length - 1
                    ? null
                    : () => move(index, index + 1),
              ),
            ),
    );
  }
}

class _CategoryTile extends StatelessWidget {
  const _CategoryTile({
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
    final translations = context.t;
    final label = category.label(translations);
    final orderTranslations = translations.shoppingList.categoryOrder;
    return ListTile(
      leading: MarketCategoryTile(category: category),
      title: Text(label),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          IconButton(
            tooltip: orderTranslations.moveUp(name: label),
            icon: const Icon(Icons.keyboard_arrow_up),
            onPressed: onMoveUp,
          ),
          IconButton(
            tooltip: orderTranslations.moveDown(name: label),
            icon: const Icon(Icons.keyboard_arrow_down),
            onPressed: onMoveDown,
          ),
          ReorderableDragStartListener(
            index: index,
            child: const ExcludeSemantics(
              child: Padding(
                padding: EdgeInsetsDirectional.all(AppSpacing.space8),
                child: Icon(Icons.drag_handle),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
