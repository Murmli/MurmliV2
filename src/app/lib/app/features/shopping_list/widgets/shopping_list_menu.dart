import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';

enum _ListAction { sortCategories, settings }

Future<void> showShoppingListMenu(BuildContext context) async {
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
    case _ListAction.settings:
      await context.router.push(const SettingsRoute());
    case null:
      break;
  }
}

class _ShoppingListMenu extends StatelessWidget {
  const _ShoppingListMenu();

  @override
  Widget build(BuildContext context) {
    final translations = context.t;
    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            leading: const Icon(Icons.swap_vert),
            title: Text(translations.shoppingList.menu.sortCategories),
            onTap: () => Navigator.of(context).pop(_ListAction.sortCategories),
          ),
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
