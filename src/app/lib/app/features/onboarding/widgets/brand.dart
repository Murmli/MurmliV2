import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/theme/app_typography.dart';

class Brand extends StatelessWidget {
  const Brand({super.key});

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final translations = context.t.app;
    return Row(
      spacing: AppSpacing.space12,
      children: [
        Container(
          width: 44,
          height: 44,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: colors.primary,
            borderRadius: BorderRadius.circular(AppRadius.brandMark),
          ),
          child: Text(
            translations.logoInitial,
            style: AppTypography.brand.copyWith(
              fontWeight: FontWeight.w800,
              color: colors.onPrimary,
            ),
          ),
        ),
        Text(
          translations.wordmark,
          style: AppTypography.wordmark.copyWith(color: colors.ink),
        ),
      ],
    );
  }
}
