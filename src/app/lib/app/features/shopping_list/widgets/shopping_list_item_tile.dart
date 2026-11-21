import 'package:material_ui/material_ui.dart';

import '../../../core/sync/app_database.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListItemTile extends StatelessWidget {
  const ShoppingListItemTile({
    required this.item,
    required this.onToggle,
    required this.onDelete,
    super.key,
  });

  final ShoppingListItem item;
  final VoidCallback onToggle;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final colorScheme = ColorScheme.of(context);
    final quantity = [
      if (item.quantity case final quantity?) _formatQuantity(quantity),
      ?item.unit,
    ].join(' ');
    return Dismissible(
      key: ValueKey(item.id),
      direction: DismissDirection.endToStart,
      onDismissed: (_) => onDelete(),
      background: ColoredBox(
        color: colorScheme.errorContainer,
        child: Align(
          alignment: AlignmentDirectional.centerEnd,
          child: Padding(
            padding: const EdgeInsetsDirectional.only(end: AppSpacing.space20),
            child: Icon(
              Icons.delete_outline,
              color: colorScheme.onErrorContainer,
            ),
          ),
        ),
      ),
      child: CheckboxListTile(
        controlAffinity: ListTileControlAffinity.leading,
        value: item.checked != 0,
        onChanged: (_) => onToggle(),
        title: Text(item.name),
        subtitle: quantity.isEmpty ? null : Text(quantity),
      ),
    );
  }
}

String _formatQuantity(double quantity) => quantity == quantity.roundToDouble()
    ? quantity.toInt().toString()
    : quantity.toString();
