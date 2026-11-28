import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import '../models/shopping_list_item.dart';
import '../providers/shopping_list_overview_provider.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';

Future<void> showShoppingListItemEditor(
  BuildContext context,
  ShoppingListItem item,
) => showModalBottomSheet<void>(
  context: context,
  useRootNavigator: true,
  isScrollControlled: true,
  builder: (_) => _ItemEditor(item: item),
);

class _ItemEditor extends HookConsumerWidget {
  const _ItemEditor({required this.item});

  final ShoppingListItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t;
    final editorTranslations = translations.shoppingList.editor;
    final languageCode = TranslationProvider.of(context).locale.languageCode;
    final nameController = useTextEditingController(text: item.name);
    final quantityController = useTextEditingController(
      text: switch (item.quantity) {
        final quantity? => formatQuantity(quantity, languageCode),
        null => '',
      },
    );
    useListenable(nameController);
    useListenable(quantityController);
    final unit = useState(item.unit);
    final category = useState(item.category);
    final categoryOrder =
        ref.watch(marketCategoryOrderProvider).value ?? MarketCategory.values;
    final name = nameController.text.trim();
    final quantityText = quantityController.text.trim();
    final quantity = quantityText.isEmpty
        ? null
        : double.tryParse(quantityText.replaceAll(',', '.'));
    final isQuantityValid =
        quantityText.isEmpty || (quantity != null && quantity > 0);

    Future<void> save() async {
      await ref
          .read(shoppingListRepositoryProvider)
          .updateItem(
            item.id,
            name: name,
            quantity: quantity,
            unit: unit.value,
            category: category.value,
          );
      if (context.mounted) {
        Navigator.of(context).pop();
      }
    }

    return Padding(
      padding: EdgeInsetsDirectional.only(
        bottom: MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsetsDirectional.all(AppSpacing.screenMargin),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisSize: MainAxisSize.min,
            spacing: AppSpacing.space16,
            children: [
              Text(
                editorTranslations.title,
                style: TextTheme.of(context).titleLarge,
              ),
              TextField(
                controller: nameController,
                decoration: InputDecoration(labelText: editorTranslations.name),
                inputFormatters: [LengthLimitingTextInputFormatter(256)],
                textCapitalization: TextCapitalization.sentences,
              ),
              TextField(
                controller: quantityController,
                decoration: InputDecoration(
                  labelText: editorTranslations.quantity,
                  errorText: isQuantityValid
                      ? null
                      : editorTranslations.invalidQuantity,
                ),
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp('[0-9.,]')),
                ],
              ),
              DropdownMenu<MeasurementUnit?>(
                initialSelection: unit.value,
                label: Text(editorTranslations.unit),
                expandedInsets: EdgeInsets.zero,
                requestFocusOnTap: false,
                onSelected: (selected) => unit.value = selected,
                dropdownMenuEntries: [
                  DropdownMenuEntry(
                    value: null,
                    label: editorTranslations.noUnit,
                  ),
                  for (final measurementUnit in MeasurementUnit.values)
                    DropdownMenuEntry(
                      value: measurementUnit,
                      label: measurementUnit.label(translations),
                    ),
                ],
              ),
              DropdownMenu<MarketCategory>(
                initialSelection: category.value,
                label: Text(editorTranslations.category),
                expandedInsets: EdgeInsets.zero,
                requestFocusOnTap: false,
                onSelected: (selected) {
                  if (selected != null) {
                    category.value = selected;
                  }
                },
                dropdownMenuEntries: [
                  for (final marketCategory in categoryOrder)
                    DropdownMenuEntry(
                      value: marketCategory,
                      label: marketCategory.label(translations),
                      leadingIcon: MarketCategoryTile(
                        category: marketCategory,
                        size: 28,
                      ),
                    ),
                ],
              ),
              FilledButton(
                onPressed: name.isNotEmpty && isQuantityValid
                    ? () => unawaited(save())
                    : null,
                child: Text(editorTranslations.save),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
