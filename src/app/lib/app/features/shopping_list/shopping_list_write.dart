import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/sync/sync_core.dart';
import 'providers/shopping_list_repository.dart';

/// Runs a write of the shopping list started by the user and returns whether
/// it went through. A limit of the server shows its reason in a snack bar.
Future<bool> runShoppingListWrite(
  BuildContext context,
  Future<void> Function() write,
) async {
  final messenger = ScaffoldMessenger.of(context);
  final limits = context.t.shoppingList.limits;
  try {
    await write();
    return true;
  } on ShoppingListLimitExceededException catch (exception) {
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(switch (exception.limit) {
            ShoppingListLimit.itemName => limits.itemName,
            ShoppingListLimit.quantity => limits.quantity,
            ShoppingListLimit.itemsPerList => limits.itemsPerList,
          }),
        ),
      );
    return false;
  } on SyncNotReadyException {
    // Only a write racing the sign-out gets here, the page shows its loading
    // state before the first sync anyway.
    return false;
  }
}
