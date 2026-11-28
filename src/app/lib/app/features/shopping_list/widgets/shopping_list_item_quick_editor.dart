import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/market_category.dart';
import '../models/shopping_list_item.dart';
import '../providers/shopping_list_overview_provider.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';
import 'shopping_list_item_editor.dart';

Future<void> showShoppingListItemQuickEditor(
  BuildContext context,
  ShoppingListItem item,
) async {
  final opensDetails = await showModalBottomSheet<bool>(
    context: context,
    useRootNavigator: true,
    isScrollControlled: true,
    builder: (_) => _QuickEditor(item: item),
  );
  if (opensDetails == true && context.mounted) {
    await showShoppingListItemEditor(context, item);
  }
}

class _QuickEditor extends HookConsumerWidget {
  const _QuickEditor({required this.item});

  final ShoppingListItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t;
    final editorTranslations = translations.shoppingList.editor;
    final nameController = useTextEditingController(text: item.name);
    final quantityController = useTextEditingController(
      text: switch (item.quantity) {
        final quantity? => formatQuantity(
          quantity,
          TranslationProvider.of(context).locale.languageCode,
        ),
        null => '',
      },
    );
    useListenable(nameController);
    useListenable(quantityController);
    final category = useState(item.category);
    final name = nameController.text.trim();
    final quantityText = quantityController.text.trim();
    final quantity = quantityText.isEmpty
        ? null
        : double.tryParse(quantityText.replaceAll(',', '.'));
    final isQuantityValid =
        quantityText.isEmpty || (quantity != null && quantity > 0);
    final isValid = name.isNotEmpty && isQuantityValid;

    Future<void> save() async {
      await ref
          .read(shoppingListRepositoryProvider)
          .updateItem(
            item.id,
            name: name,
            quantity: quantity,
            unit: item.unit,
            category: category.value,
          );
      if (context.mounted) {
        Navigator.of(context).pop();
      }
    }

    Future<void> delete() async {
      await ref.read(shoppingListRepositoryProvider).deleteItem(item.id);
      if (context.mounted) {
        Navigator.of(context).pop();
      }
    }

    return Padding(
      padding: EdgeInsetsDirectional.only(
        bottom: MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsetsDirectional.all(AppSpacing.screenMargin),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            spacing: AppSpacing.space12,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                spacing: AppSpacing.space12,
                children: [
                  Expanded(
                    child: TextField(
                      controller: nameController,
                      autofocus: true,
                      decoration: InputDecoration(
                        labelText: editorTranslations.name,
                      ),
                      inputFormatters: [LengthLimitingTextInputFormatter(256)],
                      textCapitalization: TextCapitalization.sentences,
                      textInputAction: TextInputAction.next,
                    ),
                  ),
                  SizedBox(
                    width: 96,
                    child: TextField(
                      controller: quantityController,
                      decoration: InputDecoration(
                        labelText: editorTranslations.quantity,
                        suffixText: item.unit?.abbreviation(translations),
                        errorText: isQuantityValid
                            ? null
                            : editorTranslations.invalidQuantity,
                        errorMaxLines: 4,
                      ),
                      keyboardType: const TextInputType.numberWithOptions(
                        decimal: true,
                      ),
                      inputFormatters: [
                        FilteringTextInputFormatter.allow(RegExp('[0-9.,]')),
                      ],
                      textInputAction: TextInputAction.done,
                      onSubmitted: (_) {
                        if (isValid) {
                          unawaited(save());
                        }
                      },
                    ),
                  ),
                ],
              ),
              Row(
                spacing: AppSpacing.space4,
                children: [
                  _CategoryPicker(
                    category: category.value,
                    onSelected: (selected) => category.value = selected,
                  ),
                  TextButton(
                    onPressed: () => Navigator.of(context).pop(true),
                    child: Text(translations.shoppingList.quickEditor.details),
                  ),
                  const Spacer(),
                  IconButton(
                    icon: const Icon(Icons.delete_outline),
                    color: ColorScheme.of(context).error,
                    tooltip: translations.shoppingList.item.delete,
                    onPressed: () => unawaited(delete()),
                  ),
                  IconButton.filled(
                    icon: const Icon(Icons.check),
                    tooltip: editorTranslations.save,
                    onPressed: isValid ? () => unawaited(save()) : null,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _CategoryPicker extends ConsumerWidget {
  const _CategoryPicker({required this.category, required this.onSelected});

  final MarketCategory category;
  final ValueChanged<MarketCategory> onSelected;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t;
    final categoryOrder =
        ref.watch(marketCategoryOrderProvider).value ?? MarketCategory.values;
    return MenuAnchor(
      menuChildren: [
        for (final marketCategory in categoryOrder)
          MenuItemButton(
            leadingIcon: MarketCategoryTile(category: marketCategory, size: 28),
            onPressed: () => onSelected(marketCategory),
            child: Text(marketCategory.label(translations)),
          ),
      ],
      builder: (context, controller, _) => IconButton(
        icon: MarketCategoryTile(category: category, size: 32),
        tooltip: translations.shoppingList.quickEditor.category,
        onPressed: () =>
            controller.isOpen ? controller.close() : controller.open(),
      ),
    );
  }
}
