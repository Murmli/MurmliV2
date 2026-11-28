import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../home/home_tab.dart';

class ShoppingListEmptyState extends StatelessWidget {
  const ShoppingListEmptyState({super.key});

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
              context.t.shoppingList.empty.title,
              style: textTheme.titleLarge,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: AppSpacing.space4),
            Text(
              context.t.shoppingList.empty.hint,
              style: textTheme.bodyMedium?.copyWith(color: colors.inkSecondary),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
