import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/theme/app_typography.dart';
import '../home_tab.dart';

class TabPlaceholder extends StatelessWidget {
  const TabPlaceholder({required this.tab, super.key});

  final HomeTab tab;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
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
              child: Icon(tab.icon, size: 30, color: colors.primaryText),
            ),
            const SizedBox(height: AppSpacing.space16),
            Text(
              tab.title(context.t),
              style: AppTypography.title.copyWith(color: colors.ink),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: AppSpacing.space4),
            Text(
              context.t.home.comingSoon,
              style: AppTypography.body.copyWith(color: colors.inkSecondary),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
