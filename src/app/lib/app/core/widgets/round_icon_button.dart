import 'package:material_ui/material_ui.dart';

import '../theme/app_colors.dart';

class RoundIconButton extends StatelessWidget {
  const RoundIconButton({
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.iconSize,
    super.key,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;
  final double? iconSize;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return DecoratedBox(
      decoration: ShapeDecoration(
        shape: const CircleBorder(),
        color: colors.surface,
        shadows: colors.cardShadow,
      ),
      child: IconButton(
        tooltip: tooltip,
        constraints: BoxConstraints.tight(const Size.square(48)),
        iconSize: iconSize,
        color: colors.ink,
        icon: Icon(icon),
        onPressed: onPressed,
      ),
    );
  }
}
