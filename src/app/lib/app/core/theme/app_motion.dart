import 'package:material_ui/material_ui.dart';

abstract final class AppMotion {
  static Duration short(BuildContext context) =>
      MediaQuery.disableAnimationsOf(context)
      ? Duration.zero
      : const Duration(milliseconds: 200);
}
