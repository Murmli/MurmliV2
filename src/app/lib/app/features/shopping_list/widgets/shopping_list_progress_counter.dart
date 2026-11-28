import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListProgressCounter extends StatelessWidget {
  const ShoppingListProgressCounter({
    required this.checkedCount,
    required this.totalCount,
    super.key,
  });

  final int checkedCount;
  final int totalCount;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Semantics(
      label: context.t.shoppingList.progress(
        checked: checkedCount,
        total: totalCount,
      ),
      excludeSemantics: true,
      child: DecoratedBox(
        decoration: ShapeDecoration(
          color: colors.surfaceSecondary,
          shape: const StadiumBorder(),
        ),
        child: Padding(
          padding: const EdgeInsetsDirectional.symmetric(
            horizontal: AppSpacing.space8,
            vertical: 2,
          ),
          child: Text(
            '$checkedCount/$totalCount',
            style: TextTheme.of(context).labelLarge
                ?.copyWith(color: colors.inkSecondary),
          ),
        ),
      ),
    );
  }
}
