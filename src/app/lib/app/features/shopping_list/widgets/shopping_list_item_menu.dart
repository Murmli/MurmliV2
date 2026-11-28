import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../models/shopping_list_item.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';

enum _ItemAction { delete }

Future<void> showShoppingListItemMenu(
  BuildContext context,
  WidgetRef ref,
  ShoppingListItem item,
) async {
  final action = await showModalBottomSheet<_ItemAction>(
    context: context,
    useRootNavigator: true,
    builder: (_) => _ItemMenu(item: item),
  );
  if (action == _ItemAction.delete && context.mounted) {
    await ref.read(shoppingListRepositoryProvider).deleteItem(item.id);
  }
}

class _ItemMenu extends StatelessWidget {
  const _ItemMenu({required this.item});

  final ShoppingListItem item;

  @override
  Widget build(BuildContext context) {
    final translations = context.t;
    final quantity = quantityLabel(
      item,
      translations,
      TranslationProvider.of(context).locale.languageCode,
    );
    final errorColor = ColorScheme.of(context).error;
    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            leading: MarketCategoryTile(category: item.category),
            title: Text(item.name),
            subtitle: quantity.isEmpty ? null : Text(quantity),
          ),
          const Divider(),
          ListTile(
            leading: const Icon(Icons.delete_outline),
            title: Text(translations.shoppingList.item.delete),
            iconColor: errorColor,
            textColor: errorColor,
            onTap: () => Navigator.of(context).pop(_ItemAction.delete),
          ),
        ],
      ),
    );
  }
}
