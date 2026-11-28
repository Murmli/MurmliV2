import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListAllCheckedBanner extends StatelessWidget {
  const ShoppingListAllCheckedBanner({super.key});

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final allChecked = context.t.shoppingList.allChecked;
    return Padding(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: AppSpacing.space12,
      ),
      child: MergeSemantics(
        child: Container(
          padding: const EdgeInsets.all(AppSpacing.space16),
          decoration: BoxDecoration(
            color: colors.surface,
            borderRadius: BorderRadius.circular(AppRadius.listGroup),
            boxShadow: colors.cardShadow,
          ),
          child: Row(
            spacing: AppSpacing.space12,
            children: [
              Icon(Icons.check_circle_rounded, color: colors.onTarget),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(allChecked.title, style: textTheme.titleMedium),
                    Text(
                      allChecked.hint,
                      style: textTheme.bodyMedium?.copyWith(
                        color: colors.inkSecondary,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
