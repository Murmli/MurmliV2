import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';

class RecipeDot extends StatelessWidget {
  const RecipeDot({this.border, super.key});

  final Color? border;

  @override
  Widget build(BuildContext context) => Container(
    width: 6,
    height: 6,
    decoration: BoxDecoration(
      color: context.colors.primary,
      shape: BoxShape.circle,
      border: switch (border) {
        final color? => Border.all(
          color: color,
          width: 1.5,
          strokeAlign: BorderSide.strokeAlignOutside,
        ),
        null => null,
      },
    ),
  );
}
