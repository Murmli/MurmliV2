import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class SettingsSection extends StatelessWidget {
  const SettingsSection({
    required this.title,
    required this.children,
    super.key,
  });

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      spacing: AppSpacing.space8,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.space4),
          child: Text(
            title,
            style: TextTheme.of(context).labelLarge
                ?.copyWith(color: colors.inkSecondary),
          ),
        ),
        DecoratedBox(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(AppRadius.card),
            boxShadow: colors.cardShadow,
          ),
          child: Material(
            color: colors.surface,
            borderRadius: BorderRadius.circular(AppRadius.card),
            clipBehavior: Clip.antiAlias,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.space8),
              child: Column(
                children: [
                  for (final (index, child) in children.indexed) ...[
                    if (index > 0)
                      const Divider(
                        indent: AppSpacing.space16,
                        endIndent: AppSpacing.space16,
                      ),
                    child,
                  ],
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
