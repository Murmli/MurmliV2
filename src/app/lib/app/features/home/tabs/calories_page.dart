import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/build_mode/debug_build_provider.dart';
import '../../../core/flavor/flavor.dart';
import '../../../core/flavor/flavor_provider.dart';
import '../../../core/theme/app_spacing.dart';
import '../../shopping_list/providers/shopping_list_overview_provider.dart';
import '../../shopping_list/providers/shopping_list_repository.dart';
import '../../shopping_list/sample_shopping_list.dart';
import '../../shopping_list/shopping_list_write.dart';
import '../home_tab.dart';
import 'tab_placeholder.dart';

@RoutePage()
class CaloriesPage extends ConsumerWidget {
  const CaloriesPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    const placeholder = TabPlaceholder(tab: HomeTab.calories);
    if (!ref.watch(debugBuildProvider) &&
        ref.watch(flavorProvider) != Flavor.local) {
      return placeholder;
    }
    final listId = ref.watch(currentShoppingListProvider).value?.id;
    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        placeholder,
        const SizedBox(height: AppSpacing.space16),
        OutlinedButton(
          onPressed: listId == null
              ? null
              : () => unawaited(
                  runShoppingListWrite(
                    context,
                    () => addSampleShoppingListItems(
                      ref.read(shoppingListRepositoryProvider),
                      listId: listId,
                    ),
                  ),
                ),
          child: Text(context.t.home.tabs.calories.addSampleItems),
        ),
      ],
    );
  }
}
