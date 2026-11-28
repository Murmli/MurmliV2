import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../providers/shopping_list_overview_provider.dart';
import '../shopping_list_cleanup.dart';
import '../shopping_list_name.dart';
import 'count_badge.dart';
import 'shopping_list_sheet_entry.dart';

enum _ListAction { sortCategories, deleteChecked, deleteAll, settings }

Future<void> showShoppingListMenu(BuildContext context, WidgetRef ref) async {
  final action = await showModalBottomSheet<_ListAction>(
    context: context,
    useRootNavigator: true,
    isScrollControlled: true,
    builder: (_) => const _ShoppingListMenu(),
  );
  if (!context.mounted) {
    return;
  }
  switch (action) {
    case _ListAction.sortCategories:
      await context.router.push(const CategoryOrderRoute());
    case _ListAction.deleteChecked:
      await confirmAndDeleteCheckedItems(context, ref);
    case _ListAction.deleteAll:
      await confirmAndDeleteAllItems(context, ref);
    case _ListAction.settings:
      await context.router.push(const SettingsRoute());
    case null:
      break;
  }
}

class _ShoppingListMenu extends ConsumerWidget {
  const _ShoppingListMenu();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t;
    final menu = translations.shoppingList.menu;
    final list = ref.watch(currentShoppingListProvider).value;
    final items = list?.items ?? [];
    final checkedCount = items.where((item) => item.checked).length;

    void close(_ListAction action) => Navigator.of(context).pop(action);

    return SingleChildScrollView(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: 34,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsetsDirectional.only(
              top: AppSpacing.space8,
              bottom: AppSpacing.space4,
            ),
            child: Semantics(
              header: true,
              child: Text(
                shoppingListDisplayName(list?.name ?? '', translations),
                style: TextTheme.of(context).headlineSmall!.copyWith(
                  fontSize: 22,
                  height: 28 / 22,
                  fontVariations: const [FontVariation.opticalSize(22)],
                ),
              ),
            ),
          ),
          _SectionHeading(menu.sectionCleanup),
          _CountedEntry(
            icon: Icons.checklist_rtl,
            label: menu.deleteChecked,
            count: checkedCount,
            onTap: () => close(_ListAction.deleteChecked),
          ),
          _CountedEntry(
            icon: Icons.delete_sweep_outlined,
            label: menu.deleteAll,
            count: items.length,
            onTap: () => close(_ListAction.deleteAll),
          ),
          const Divider(),
          _SectionHeading(menu.sectionList),
          ShoppingListSheetEntry(
            icon: Icons.swap_vert,
            label: menu.sortCategories,
            onTap: () => close(_ListAction.sortCategories),
          ),
          const Divider(),
          ShoppingListSheetEntry(
            icon: Icons.settings_outlined,
            label: translations.settings.title,
            onTap: () => close(_ListAction.settings),
          ),
        ],
      ),
    );
  }
}

class _SectionHeading extends StatelessWidget {
  const _SectionHeading(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsetsDirectional.only(
      top: AppSpacing.space12,
      bottom: AppSpacing.space4,
    ),
    child: Semantics(
      header: true,
      child: Text(
        text.toUpperCase(),
        style: TextTheme.of(context).labelSmall!.copyWith(
          fontWeight: FontWeight.w700,
          letterSpacing: 1.1,
          color: context.colors.inkTertiary,
        ),
      ),
    ),
  );
}

/// Disabled while [count] is zero.
class _CountedEntry extends StatelessWidget {
  const _CountedEntry({
    required this.icon,
    required this.label,
    required this.count,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final hasItems = count > 0;
    return ShoppingListSheetEntry(
      icon: icon,
      label: label,
      onTap: hasItems ? onTap : null,
      trailing: hasItems ? CountBadge(count) : null,
      semanticsValue: hasItems
          ? context.t.shoppingList.menu.itemCount(n: count)
          : null,
    );
  }
}
