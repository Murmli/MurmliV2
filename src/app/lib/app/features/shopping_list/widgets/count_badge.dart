import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class CountBadge extends StatelessWidget {
  const CountBadge(this.count, {super.key});

  final int count;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return ConstrainedBox(
      constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
      child: DecoratedBox(
        decoration: ShapeDecoration(
          color: colors.surfaceSecondary,
          shape: const StadiumBorder(),
        ),
        child: Padding(
          padding: const EdgeInsetsDirectional.symmetric(
            horizontal: AppSpacing.space8,
          ),
          child: Center(
            widthFactor: 1,
            heightFactor: 1,
            child: Text(
              '$count',
              style: TextTheme.of(context).labelSmall!.copyWith(
                fontSize: 12,
                height: 16 / 12,
                fontWeight: FontWeight.w700,
                color: colors.inkSecondary,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
