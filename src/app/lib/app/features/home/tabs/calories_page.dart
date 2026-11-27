import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/build_mode/debug_build_provider.dart';
import '../../../core/theme/app_spacing.dart';
import '../../shopping_list/providers/first_shopping_list_provider.dart';
import '../home_tab.dart';
import 'tab_placeholder.dart';

@RoutePage()
class CaloriesPage extends ConsumerWidget {
  const CaloriesPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    const placeholder = TabPlaceholder(tab: HomeTab.calories);
    if (!ref.watch(debugBuildProvider)) {
      return placeholder;
    }
    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        placeholder,
        const SizedBox(height: AppSpacing.space16),
        OutlinedButton(
          onPressed: () => unawaited(
            ref.read(firstShoppingListProvider.notifier).resetSampleData(),
          ),
          child: Text(context.t.home.tabs.calories.resetSampleData),
        ),
      ],
    );
  }
}
