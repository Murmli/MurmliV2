import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_spacing.dart';
import '../../shopping_list/providers/shopping_list_overview_provider.dart';
import '../../shopping_list/widgets/shopping_list_empty_state.dart';
import '../../shopping_list/widgets/shopping_list_group_card.dart';
import '../../shopping_list/widgets/shopping_list_progress.dart';

@RoutePage()
class ShoppingListPage extends ConsumerWidget {
  const ShoppingListPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final overview = ref.watch(shoppingListOverviewProvider);
    if (overview == null) {
      return const Center(child: CircularProgressIndicator());
    }
    return Column(
      children: [
        if (overview.totalCount > 0)
          ShoppingListProgress(
            checkedCount: overview.checkedCount,
            totalCount: overview.totalCount,
          ),
        Expanded(
          child: overview.groups.isEmpty
              ? const ShoppingListEmptyState()
              : ListView.builder(
                  padding: EdgeInsetsDirectional.only(
                    bottom:
                        MediaQuery.paddingOf(context).bottom +
                        AppSpacing.space16,
                  ),
                  itemCount: overview.groups.length,
                  itemBuilder: (context, index) =>
                      ShoppingListGroupCard(group: overview.groups[index]),
                ),
        ),
      ],
    );
  }
}
