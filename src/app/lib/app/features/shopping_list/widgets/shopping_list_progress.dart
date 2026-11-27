import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListProgress extends StatelessWidget {
  const ShoppingListProgress({
    required this.checkedCount,
    required this.totalCount,
    super.key,
  });

  final int checkedCount;
  final int totalCount;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Padding(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: AppSpacing.space12,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        spacing: AppSpacing.space8,
        children: [
          Text(
            context.t.shoppingList.progress(
              checked: checkedCount,
              total: totalCount,
            ),
            style: TextTheme.of(context).labelLarge
                ?.copyWith(color: colors.inkSecondary),
          ),
          ExcludeSemantics(
            child: Container(
              height: 6,
              decoration: BoxDecoration(
                color: colors.surfaceSecondary,
                borderRadius: BorderRadius.circular(3),
              ),
              child: AnimatedFractionallySizedBox(
                duration: AppMotion.short(context),
                alignment: AlignmentDirectional.centerStart,
                widthFactor: checkedCount / totalCount,
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: colors.onTarget,
                    borderRadius: BorderRadius.circular(3),
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
