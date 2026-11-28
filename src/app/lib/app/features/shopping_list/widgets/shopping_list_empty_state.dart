import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../home/home_tab.dart';

class ShoppingListEmptyState extends StatelessWidget {
  const ShoppingListEmptyState({super.key});

  @override
  Widget build(BuildContext context) => _StateMessage(
    title: context.t.shoppingList.empty.title,
    hint: context.t.shoppingList.empty.hint,
  );
}

class ShoppingListLoadingState extends StatelessWidget {
  const ShoppingListLoadingState({required this.showsOfflineHint, super.key});

  final bool showsOfflineHint;

  @override
  Widget build(BuildContext context) => _StateMessage(
    title: context.t.shoppingList.loading.title,
    hint: showsOfflineHint ? context.t.shoppingList.loading.offline : null,
  );
}

class _StateMessage extends StatelessWidget {
  const _StateMessage({required this.title, required this.hint});

  final String title;
  final String? hint;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.screenMargin),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(
                color: colors.primarySoft,
                borderRadius: BorderRadius.circular(AppRadius.card),
              ),
              child: Icon(
                HomeTab.shoppingList.icon,
                size: 30,
                color: colors.primaryText,
              ),
            ),
            const SizedBox(height: AppSpacing.space16),
            Text(
              title,
              style: textTheme.titleLarge,
              textAlign: TextAlign.center,
            ),
            if (hint case final hint?) ...[
              const SizedBox(height: AppSpacing.space4),
              Text(
                hint,
                style: textTheme.bodyMedium?.copyWith(
                  color: colors.inkSecondary,
                ),
                textAlign: TextAlign.center,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
