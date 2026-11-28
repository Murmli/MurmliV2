import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import '../models/shopping_list_item.dart';
import '../providers/shopping_list_overview_provider.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';

const _fixedUnits = [
  MeasurementUnit.piece,
  MeasurementUnit.gram,
  MeasurementUnit.kilogram,
];

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
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final editorTranslations = context.t.shoppingList.editor;
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
    final name = nameController.text.trim();
    final (:quantity, isValid: isQuantityValid) = parseQuantity(
      quantityController.text,
    );
    final buttonStyle = ButtonStyle(
      minimumSize: const WidgetStatePropertyAll(Size.fromHeight(48)),
      textStyle: WidgetStatePropertyAll(
        textTheme.bodyMedium!.copyWith(fontWeight: FontWeight.w700),
      ),
    );

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
      child: SingleChildScrollView(
        padding: const EdgeInsetsDirectional.only(
          start: AppSpacing.screenMargin,
          top: AppSpacing.space8,
          end: AppSpacing.screenMargin,
          bottom: 34,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          spacing: AppSpacing.space16,
          children: [
            Semantics(
              header: true,
              child: Text(
                editorTranslations.title,
                style: textTheme.headlineSmall!.copyWith(
                  fontSize: 22,
                  height: 28 / 22,
                  fontVariations: const [FontVariation.opticalSize(22)],
                ),
              ),
            ),
            _LabeledField(
              label: editorTranslations.name,
              controller: nameController,
              inputFormatters: [LengthLimitingTextInputFormatter(256)],
              textCapitalization: TextCapitalization.sentences,
            ),
            Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              spacing: AppSpacing.space8,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  spacing: AppSpacing.space12,
                  children: [
                    SizedBox(
                      width: 96,
                      child: _LabeledField(
                        label: editorTranslations.quantity,
                        controller: quantityController,
                        isInvalid: !isQuantityValid,
                        keyboardType: const TextInputType.numberWithOptions(
                          decimal: true,
                        ),
                        inputFormatters: [
                          FilteringTextInputFormatter.allow(RegExp('[0-9.,]')),
                        ],
                      ),
                    ),
                    Expanded(
                      child: _UnitChoice(
                        unit: unit.value,
                        onChanged: (selected) => unit.value = selected,
                      ),
                    ),
                  ],
                ),
                Text(
                  isQuantityValid
                      ? editorTranslations.hint
                      : editorTranslations.invalidQuantity,
                  style: textTheme.bodySmall!.copyWith(
                    fontSize: 12,
                    height: 16 / 12,
                    color: isQuantityValid
                        ? colors.inkSecondary
                        : colors.categoryRed,
                  ),
                ),
              ],
            ),
            _CategoryChoice(
              category: category.value,
              onSelected: (selected) => category.value = selected,
            ),
            Padding(
              padding: const EdgeInsetsDirectional.only(top: AppSpacing.space4),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                spacing: AppSpacing.space8,
                children: [
                  FilledButton(
                    style: buttonStyle,
                    onPressed: name.isNotEmpty && isQuantityValid
                        ? () => unawaited(save())
                        : null,
                    child: Text(editorTranslations.save),
                  ),
                  OutlinedButton(
                    style: buttonStyle,
                    onPressed: () => Navigator.of(context).pop(),
                    child: Text(editorTranslations.cancel),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _FieldLabel extends StatelessWidget {
  const _FieldLabel(this.label);

  final String label;

  @override
  Widget build(BuildContext context) => Text(
    label,
    style: TextTheme.of(context).labelLarge!.copyWith(
      fontWeight: FontWeight.w700,
      color: context.colors.inkSecondary,
    ),
  );
}

class _LabeledField extends StatelessWidget {
  const _LabeledField({
    required this.label,
    required this.controller,
    required this.inputFormatters,
    this.isInvalid = false,
    this.keyboardType,
    this.textCapitalization = TextCapitalization.none,
  });

  final String label;
  final TextEditingController controller;
  final List<TextInputFormatter> inputFormatters;
  final bool isInvalid;
  final TextInputType? keyboardType;
  final TextCapitalization textCapitalization;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    OutlineInputBorder border(Color color, double width) => OutlineInputBorder(
      borderRadius: BorderRadius.circular(16),
      borderSide: BorderSide(color: color, width: width),
    );
    return MergeSemantics(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        spacing: 6,
        children: [
          _FieldLabel(label),
          TextField(
            controller: controller,
            keyboardType: keyboardType,
            inputFormatters: inputFormatters,
            textCapitalization: textCapitalization,
            style: TextTheme.of(context).bodyMedium!
                .copyWith(fontWeight: FontWeight.w600),
            decoration: InputDecoration(
              isDense: true,
              filled: true,
              fillColor: colors.background,
              contentPadding: const EdgeInsetsDirectional.symmetric(
                horizontal: AppSpacing.space16,
                vertical: 15,
              ),
              enabledBorder: border(
                isInvalid ? colors.categoryRed : colors.line,
                1,
              ),
              focusedBorder: border(
                isInvalid ? colors.categoryRed : colors.primary,
                1.5,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _UnitChoice extends StatelessWidget {
  const _UnitChoice({required this.unit, required this.onChanged});

  final MeasurementUnit? unit;
  final ValueChanged<MeasurementUnit?> onChanged;

  @override
  Widget build(BuildContext context) {
    final translations = context.t;
    final selectedUnit = unit;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      spacing: 6,
      children: [
        _FieldLabel(translations.shoppingList.editor.unit),
        Wrap(
          spacing: AppSpacing.space8,
          runSpacing: AppSpacing.space8,
          children: [
            for (final chipUnit in [
              ..._fixedUnits,
              if (selectedUnit != null && !_fixedUnits.contains(selectedUnit))
                selectedUnit,
            ])
              _UnitChip(
                label: chipUnit.abbreviation(translations),
                semanticsLabel: chipUnit.label(translations),
                isSelected: chipUnit == selectedUnit,
                onTap: () =>
                    onChanged(chipUnit == selectedUnit ? null : chipUnit),
              ),
            MenuAnchor(
              menuChildren: [
                for (final menuUnit in MeasurementUnit.values)
                  if (!_fixedUnits.contains(menuUnit))
                    MenuItemButton(
                      onPressed: () => onChanged(menuUnit),
                      child: Text(menuUnit.label(translations)),
                    ),
              ],
              builder: (context, controller, _) => _UnitChip(
                label: translations.shoppingList.editor.moreUnits,
                trailingIcon: Icons.expand_more,
                onTap: () =>
                    controller.isOpen ? controller.close() : controller.open(),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _UnitChip extends StatelessWidget {
  const _UnitChip({
    required this.label,
    required this.onTap,
    this.semanticsLabel,
    this.isSelected = false,
    this.trailingIcon,
  });

  final String label;
  final VoidCallback onTap;
  final String? semanticsLabel;
  final bool isSelected;
  final IconData? trailingIcon;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final foreground = isSelected ? colors.background : colors.ink;
    return Semantics(
      container: true,
      button: true,
      selected: isSelected,
      label: semanticsLabel ?? label,
      onTap: onTap,
      excludeSemantics: true,
      child: Material(
        color: isSelected ? colors.ink : colors.surface,
        shape: StadiumBorder(
          side: isSelected ? BorderSide.none : BorderSide(color: colors.line),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 44),
            child: Padding(
              padding: EdgeInsetsDirectional.symmetric(
                horizontal: trailingIcon == null ? 14 : AppSpacing.space12,
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                spacing: AppSpacing.space4,
                children: [
                  Text(
                    label,
                    style: TextTheme.of(context).labelLarge!.copyWith(
                      fontWeight: isSelected
                          ? FontWeight.w700
                          : FontWeight.w600,
                      color: foreground,
                    ),
                  ),
                  if (trailingIcon case final icon?)
                    Icon(icon, size: 14, color: foreground),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _CategoryChoice extends ConsumerWidget {
  const _CategoryChoice({required this.category, required this.onSelected});

  final MarketCategory category;
  final ValueChanged<MarketCategory> onSelected;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final translations = context.t;
    final categoryOrder =
        ref.watch(marketCategoryOrderProvider).value ?? MarketCategory.values;
    return MergeSemantics(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        spacing: 6,
        children: [
          _FieldLabel(translations.shoppingList.editor.category),
          MenuAnchor(
            menuChildren: [
              for (final marketCategory in categoryOrder)
                MenuItemButton(
                  leadingIcon: MarketCategoryTile(
                    category: marketCategory,
                    size: 28,
                  ),
                  onPressed: () => onSelected(marketCategory),
                  child: Text(marketCategory.label(translations)),
                ),
            ],
            builder: (context, controller, _) => Semantics(
              button: true,
              child: Material(
                color: colors.background,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(16),
                  side: BorderSide(color: colors.line),
                ),
                clipBehavior: Clip.antiAlias,
                child: InkWell(
                  onTap: () => controller.isOpen
                      ? controller.close()
                      : controller.open(),
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(minHeight: 56),
                    child: Padding(
                      padding: const EdgeInsetsDirectional.only(
                        start: AppSpacing.space8,
                        end: AppSpacing.space12,
                      ),
                      child: Row(
                        spacing: AppSpacing.space12,
                        children: [
                          MarketCategoryTile(category: category),
                          Expanded(
                            child: Text(
                              category.label(translations),
                              style: TextTheme.of(context).bodyMedium!
                                  .copyWith(fontWeight: FontWeight.w600),
                            ),
                          ),
                          Icon(Icons.expand_more, color: colors.inkSecondary),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
