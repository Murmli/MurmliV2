import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_item.dart';
import '../providers/shopping_list_repository.dart';
import '../quantity_label.dart';
import 'market_category_tile.dart';
import 'shopping_list_item_editor.dart';
import 'shopping_list_sheet_entry.dart';

Future<void> showShoppingListItemMenu(
  BuildContext context,
  ShoppingListItem item,
) async {
  final opensEditor = await showModalBottomSheet<bool>(
    context: context,
    useRootNavigator: true,
    isScrollControlled: true,
    builder: (_) => _ItemMenu(item: item),
  );
  if (opensEditor == true && context.mounted) {
    await showShoppingListItemEditor(context, item);
  }
}

class _ItemMenu extends ConsumerWidget {
  const _ItemMenu({required this.item});

  final ShoppingListItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final translations = context.t;
    final itemTranslations = translations.shoppingList.item;
    final textTheme = TextTheme.of(context);
    final quantity = quantityLabel(
      item,
      translations,
      TranslationProvider.of(context).locale.languageCode,
    );

    Future<void> delete() async {
      final repository = ref.read(shoppingListRepositoryProvider);
      Navigator.of(context).pop();
      await repository.deleteItem(item.id);
    }

    return SingleChildScrollView(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: 34,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        spacing: AppSpacing.space8,
        children: [
          Padding(
            padding: const EdgeInsetsDirectional.only(
              top: AppSpacing.space8,
              bottom: AppSpacing.space4,
            ),
            child: Row(
              spacing: AppSpacing.space12,
              children: [
                MarketCategoryTile(category: item.category, size: 44),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    spacing: 2,
                    children: [
                      Semantics(
                        header: true,
                        child: Text(
                          [
                            item.name,
                            if (quantity.isNotEmpty) quantity,
                          ].join(' · '),
                          style: textTheme.headlineSmall!.copyWith(
                            fontSize: 22,
                            height: 28 / 22,
                            fontVariations: const [
                              FontVariation.opticalSize(22),
                            ],
                          ),
                        ),
                      ),
                      Text(
                        item.category.label(translations),
                        style: textTheme.labelSmall!.copyWith(
                          fontSize: 12,
                          height: 16 / 12,
                          color: colors.inkSecondary,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          const Divider(),
          _MenuEntry(
            icon: Icons.edit_outlined,
            label: itemTranslations.edit,
            onTap: () => Navigator.of(context).pop(true),
          ),
          _MenuEntry(
            icon: Icons.swap_horiz,
            label: itemTranslations.alternative,
          ),
          if (item.recipeShares.isNotEmpty)
            _MenuEntry(
              icon: Icons.menu_book_outlined,
              label: itemTranslations.showRecipe,
            ),
          _MenuEntry(
            icon: Icons.delete_outline,
            label: itemTranslations.delete,
            isDestructive: true,
            onTap: () => unawaited(delete()),
          ),
        ],
      ),
    );
  }
}

/// An entry without [onTap] is a placeholder for an action that comes later.
class _MenuEntry extends StatelessWidget {
  const _MenuEntry({
    required this.icon,
    required this.label,
    this.isDestructive = false,
    this.onTap,
  });

  final IconData icon;
  final String label;
  final bool isDestructive;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final isPlaceholder = onTap == null;
    return ShoppingListSheetEntry(
      icon: icon,
      label: label,
      isDestructive: isDestructive,
      onTap: onTap,
      trailing: isPlaceholder ? const _ComingSoonPill() : null,
      semanticsLabel: isPlaceholder
          ? context.t.shoppingList.item.comingSoonLabel(action: label)
          : null,
    );
  }
}

class _ComingSoonPill extends StatelessWidget {
  const _ComingSoonPill();

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return DecoratedBox(
      decoration: ShapeDecoration(
        color: colors.surfaceSecondary,
        shape: const StadiumBorder(),
      ),
      child: Padding(
        padding: const EdgeInsetsDirectional.symmetric(
          horizontal: 10,
          vertical: AppSpacing.space4,
        ),
        child: Text(
          context.t.shoppingList.item.comingSoon,
          style: TextTheme.of(context).labelSmall!.copyWith(
            fontSize: 12,
            height: 16 / 12,
            fontWeight: FontWeight.w700,
            color: colors.inkSecondary,
          ),
        ),
      ),
    );
  }
}
