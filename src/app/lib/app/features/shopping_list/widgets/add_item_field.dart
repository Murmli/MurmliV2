import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_spacing.dart';

class AddItemField extends HookWidget {
  const AddItemField({required this.onAdd, super.key});

  final Future<void> Function(String name) onAdd;

  @override
  Widget build(BuildContext context) {
    final translations = context.t.shoppingList;
    final controller = useTextEditingController();

    Future<void> submit() async {
      final name = controller.text.trim();
      if (name.isEmpty) {
        return;
      }
      controller.clear();
      await onAdd(name);
    }

    return Row(
      spacing: AppSpacing.space8,
      children: [
        Expanded(
          child: TextField(
            controller: controller,
            textInputAction: TextInputAction.done,
            decoration: InputDecoration(hintText: translations.addItemHint),
            onSubmitted: (_) => unawaited(submit()),
          ),
        ),
        IconButton.filled(
          tooltip: translations.addItem,
          icon: const Icon(Icons.add),
          onPressed: () => unawaited(submit()),
        ),
      ],
    );
  }
}
