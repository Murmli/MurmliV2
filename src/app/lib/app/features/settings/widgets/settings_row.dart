import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/widgets/icon_tile.dart';

class SettingsRow extends StatelessWidget {
  const SettingsRow({
    required this.icon,
    required this.label,
    this.isSelected = false,
    this.trailing,
    this.onTap,
    super.key,
  });

  final IconData icon;
  final String label;
  final bool isSelected;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      leading: IconTile(icon: icon),
      title: Text(label),
      selected: isSelected,
      trailing: isSelected
          ? Icon(Icons.check, color: context.colors.primaryText)
          : trailing,
      onTap: onTap,
    );
  }
}
