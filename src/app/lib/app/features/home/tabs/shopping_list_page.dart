import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/sync/app_database.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../shopping_list/providers/shopping_list_provider.dart';
import '../../shopping_list/providers/shopping_list_repository_provider.dart';
import '../../shopping_list/widgets/add_item_field.dart';
import '../../shopping_list/widgets/shopping_list_actions_menu.dart';
import '../../shopping_list/widgets/shopping_list_item_tile.dart';
import '../../shopping_list/widgets/simulated_offline_button.dart';
import '../../shopping_list/widgets/sync_status_icon.dart';

@RoutePage()
class ShoppingListPage extends ConsumerWidget {
  const ShoppingListPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.shoppingList;
    return switch (ref.watch(shoppingListProvider)) {
      AsyncData(value: final list?) => _ShoppingListContent(list: list),
      AsyncData() => _ShoppingListMessage(text: translations.noList),
      AsyncError() => _ShoppingListMessage(text: translations.syncUnavailable),
      _ => const SizedBox.shrink(),
    };
  }
}

class _ShoppingListContent extends ConsumerWidget {
  const _ShoppingListContent({required this.list});

  final ShoppingList list;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final items = ref.watch(shoppingListItemsProvider(list.id)).value ?? [];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsetsDirectional.fromSTEB(
            AppSpacing.screenMargin,
            0,
            AppSpacing.space8,
            0,
          ),
          child: Row(
            spacing: AppSpacing.space8,
            children: [
              Expanded(
                child: Text(
                  list.name,
                  style: TextTheme.of(context).titleMedium,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const SimulatedOfflineButton(),
              const SyncStatusIcon(),
              ShoppingListActionsMenu(list: list),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.screenMargin,
            vertical: AppSpacing.space8,
          ),
          child: AddItemField(
            onAdd: (name) async =>
                (await ref.read(shoppingListRepositoryProvider.future))
                    .addItem(list, name),
          ),
        ),
        Expanded(
          child: items.isEmpty
              ? _ShoppingListMessage(text: context.t.shoppingList.empty)
              : ListView.builder(
                  padding: EdgeInsetsDirectional.only(
                    bottom:
                        MediaQuery.paddingOf(context).bottom +
                        AppSpacing.space16,
                  ),
                  itemCount: items.length,
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return ShoppingListItemTile(
                      item: item,
                      onToggle: () => unawaited(
                        ref
                            .read(shoppingListRepositoryProvider.future)
                            .then(
                              (repository) => repository.toggleChecked(item),
                            ),
                      ),
                      onRename: (name) => unawaited(
                        ref
                            .read(shoppingListRepositoryProvider.future)
                            .then(
                              (repository) => repository.renameItem(item, name),
                            ),
                      ),
                      onDelete: () => unawaited(
                        ref
                            .read(shoppingListRepositoryProvider.future)
                            .then((repository) => repository.deleteItem(item)),
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}

class _ShoppingListMessage extends StatelessWidget {
  const _ShoppingListMessage({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.screenMargin),
        child: Text(
          text,
          style: TextTheme.of(context).bodyMedium
              ?.copyWith(color: context.colors.inkSecondary),
          textAlign: TextAlign.center,
        ),
      ),
    );
  }
}
