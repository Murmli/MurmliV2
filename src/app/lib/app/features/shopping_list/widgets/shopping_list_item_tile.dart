import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/sync/app_database.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListItemTile extends StatelessWidget {
  const ShoppingListItemTile({
    required this.item,
    required this.onToggle,
    required this.onRename,
    required this.onDelete,
    super.key,
  });

  final ShoppingListItem item;
  final VoidCallback onToggle;
  final ValueChanged<String> onRename;
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
      child: ListTile(
        leading: Checkbox(
          value: item.checked != 0,
          onChanged: (_) => onToggle(),
        ),
        title: Text(item.name),
        subtitle: quantity.isEmpty ? null : Text(quantity),
        onTap: () => unawaited(_rename(context)),
      ),
    );
  }

  Future<void> _rename(BuildContext context) async {
    final name = await showDialog<String>(
      context: context,
      builder: (_) => _EditItemDialog(name: item.name),
    );
    if (name != null) {
      onRename(name);
    }
  }
}

class _EditItemDialog extends HookWidget {
  const _EditItemDialog({required this.name});

  final String name;

  @override
  Widget build(BuildContext context) {
    final translations = context.t.shoppingList;
    final controller = useTextEditingController.fromValue(
      TextEditingValue(
        text: name,
        selection: TextSelection(baseOffset: 0, extentOffset: name.length),
      ),
    );
    void save() => Navigator.of(context).pop(controller.text);
    return AlertDialog(
      title: Text(translations.editItemTitle),
      content: TextField(
        controller: controller,
        autofocus: true,
        textInputAction: TextInputAction.done,
        onSubmitted: (_) => save(),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(translations.cancel),
        ),
        FilledButton(onPressed: save, child: Text(translations.save)),
      ],
    );
  }
}

String _formatQuantity(double quantity) => quantity == quantity.roundToDouble()
    ? quantity.toInt().toString()
    : quantity.toString();
