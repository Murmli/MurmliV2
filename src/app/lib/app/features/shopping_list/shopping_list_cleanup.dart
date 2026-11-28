import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/widgets/confirmation_dialog.dart';
import 'models/shopping_list_item.dart';
import 'providers/shopping_list_overview_provider.dart';
import 'providers/shopping_list_repository.dart';

const _maximumNamedItemCount = 3;

Future<void> confirmAndDeleteCheckedItems(
  BuildContext context,
  WidgetRef ref,
) async {
  final list = ref.read(currentShoppingListProvider).value;
  if (list == null) {
    return;
  }
  final checkedItems = list.items.where((item) => item.checked).toList();
  if (checkedItems.isEmpty) {
    return;
  }
  final menu = context.t.shoppingList.menu;
  final dialog = menu.deleteCheckedDialog;
  final count = checkedItems.length;
  final confirmed = await showConfirmationDialog(
    context,
    icon: Icons.checklist_rtl,
    isDestructive: true,
    title: dialog.title(n: count),
    message: count <= _maximumNamedItemCount
        ? dialog.namedMessage(
            n: count,
            names: _joinNames(checkedItems, menu.and),
          )
        : dialog.countMessage(n: count),
    cancelLabel: dialog.cancel,
    confirmLabel: dialog.confirm(n: count),
  );
  if (confirmed) {
    await ref.read(shoppingListRepositoryProvider).deleteCheckedItems(list.id);
  }
}

Future<void> confirmAndDeleteAllItems(
  BuildContext context,
  WidgetRef ref,
) async {
  final list = ref.read(currentShoppingListProvider).value;
  if (list == null || list.items.isEmpty) {
    return;
  }
  final menu = context.t.shoppingList.menu;
  final dialog = menu.deleteAllDialog;
  final count = list.items.length;
  final confirmed = await showConfirmationDialog(
    context,
    icon: Icons.delete_sweep_outlined,
    isDestructive: true,
    title: dialog.title,
    message: count <= _maximumNamedItemCount
        ? dialog.namedMessage(n: count, names: _joinNames(list.items, menu.and))
        : dialog.countMessage(n: count),
    cancelLabel: dialog.cancel,
    confirmLabel: dialog.confirm(n: count),
  );
  if (confirmed) {
    await ref.read(shoppingListRepositoryProvider).deleteAllItems(list.id);
  }
}

String _joinNames(List<ShoppingListItem> items, String and) {
  final names = items.map((item) => item.name).toList();
  if (names.length == 1) {
    return names.single;
  }
  return '${names.take(names.length - 1).join(', ')} $and ${names.last}';
}
