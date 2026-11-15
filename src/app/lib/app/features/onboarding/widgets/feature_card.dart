import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/icon_tile.dart';

class FeatureCard extends StatelessWidget {
  const FeatureCard({super.key});

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final features = context.t.onboarding.features;
    const divider = Divider(
      indent: AppSpacing.space16,
      endIndent: AppSpacing.space16,
    );
    return Container(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.space8),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppRadius.featureCard),
        boxShadow: colors.cardShadow,
      ),
      child: Column(
        children: [
          _FeatureRow(
            icon: Icons.auto_awesome,
            title: features.assistant.title,
            subtitle: features.assistant.subtitle,
          ),
          divider,
          _FeatureRow(
            icon: Icons.local_fire_department_outlined,
            title: features.calories.title,
            subtitle: features.calories.subtitle,
          ),
          divider,
          _FeatureRow(
            icon: Icons.shopping_cart_outlined,
            title: features.shopping.title,
            subtitle: features.shopping.subtitle,
          ),
          divider,
          _FeatureRow(
            icon: Icons.calendar_today_outlined,
            title: features.planning.title,
            subtitle: features.planning.subtitle,
          ),
        ],
      ),
    );
  }
}

class _FeatureRow extends StatelessWidget {
  const _FeatureRow({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      leading: IconTile(icon: icon),
      title: Text(title),
      subtitle: Text(subtitle),
    );
  }
}
