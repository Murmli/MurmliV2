import 'package:material_ui/material_ui.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

class IconTile extends StatelessWidget {
  const IconTile({required this.icon, super.key});

  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Container(
      width: 40,
      height: 40,
      decoration: BoxDecoration(
        color: colors.primarySoft,
        borderRadius: BorderRadius.circular(AppRadius.iconTile),
      ),
      child: Icon(icon, size: 22, color: colors.primaryText),
    );
  }
}
