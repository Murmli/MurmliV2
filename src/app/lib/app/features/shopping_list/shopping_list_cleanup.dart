import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/widgets/confirmation_dialog.dart';
import 'providers/shopping_list_overview_provider.dart';
import 'providers/shopping_list_repository.dart';

Future<void> confirmAndDeleteCheckedItems(
  BuildContext context,
  WidgetRef ref,
) async {
  final list = ref.read(currentShoppingListProvider).value;
  if (list == null) {
    return;
  }
  final count = list.items.where((item) => item.checked).length;
  if (count == 0) {
    return;
  }
  final dialog = context.t.shoppingList.menu.deleteCheckedDialog;
  final confirmed = await showConfirmationDialog(
    context,
    title: dialog.title,
    message: dialog.message(n: count),
    cancelLabel: dialog.cancel,
    confirmLabel: dialog.confirm,
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
  final dialog = context.t.shoppingList.menu.deleteAllDialog;
  final confirmed = await showConfirmationDialog(
    context,
    title: dialog.title,
    message: dialog.message(n: list.items.length),
    cancelLabel: dialog.cancel,
    confirmLabel: dialog.confirm,
  );
  if (confirmed) {
    await ref.read(shoppingListRepositoryProvider).deleteAllItems(list.id);
  }
}
