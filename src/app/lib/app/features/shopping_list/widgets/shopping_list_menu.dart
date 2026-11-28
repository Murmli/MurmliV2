import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';
import '../providers/shopping_list_overview_provider.dart';
import '../shopping_list_cleanup.dart';

enum _ListAction { sortCategories, deleteChecked, deleteAll, settings }

Future<void> showShoppingListMenu(BuildContext context, WidgetRef ref) async {
  final action = await showModalBottomSheet<_ListAction>(
    context: context,
    useRootNavigator: true,
    builder: (_) => const _ShoppingListMenu(),
  );
  if (!context.mounted) {
    return;
  }
  switch (action) {
    case _ListAction.sortCategories:
      await context.router.push(const CategoryOrderRoute());
    case _ListAction.deleteChecked:
      await confirmAndDeleteCheckedItems(context, ref);
    case _ListAction.deleteAll:
      await confirmAndDeleteAllItems(context, ref);
    case _ListAction.settings:
      await context.router.push(const SettingsRoute());
    case null:
      break;
  }
}

class _ShoppingListMenu extends ConsumerWidget {
  const _ShoppingListMenu();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t;
    final menu = translations.shoppingList.menu;
    final items = ref.watch(currentShoppingListProvider).value?.items ?? [];
    final checkedCount = items.where((item) => item.checked).length;
    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            leading: const Icon(Icons.swap_vert),
            title: Text(menu.sortCategories),
            onTap: () => Navigator.of(context).pop(_ListAction.sortCategories),
          ),
          const Divider(),
          ListTile(
            leading: const Icon(Icons.checklist_rtl),
            title: Text(menu.deleteChecked),
            subtitle: checkedCount > 0
                ? Text(menu.itemCount(n: checkedCount))
                : null,
            enabled: checkedCount > 0,
            onTap: () => Navigator.of(context).pop(_ListAction.deleteChecked),
          ),
          ListTile(
            leading: const Icon(Icons.delete_sweep_outlined),
            title: Text(menu.deleteAll),
            subtitle: items.isNotEmpty
                ? Text(menu.itemCount(n: items.length))
                : null,
            enabled: items.isNotEmpty,
            onTap: () => Navigator.of(context).pop(_ListAction.deleteAll),
          ),
          const Divider(),
          ListTile(
            leading: const Icon(Icons.settings_outlined),
            title: Text(translations.settings.title),
            onTap: () => Navigator.of(context).pop(_ListAction.settings),
          ),
        ],
      ),
    );
  }
}
