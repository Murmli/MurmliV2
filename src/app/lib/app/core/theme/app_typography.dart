import 'package:material_ui/material_ui.dart';

abstract final class AppTypography {
  static const displayFontFamily = 'BricolageGrotesque';
  static const bodyFontFamily = 'PlusJakartaSans';

  // Bricolage Grotesque is variable with defaults wght 800 / opsz 96, so every
  // style pins its weight and matches the optical size to the font size.
  static const display = TextStyle(
    fontFamily: displayFontFamily,
    fontWeight: FontWeight.w700,
    fontSize: 34,
    height: 38 / 34,
    letterSpacing: -0.34,
    fontVariations: [FontVariation.opticalSize(34)],
  );

  static const title = TextStyle(
    fontFamily: displayFontFamily,
    fontWeight: FontWeight.w700,
    fontSize: 24,
    height: 30 / 24,
    fontVariations: [FontVariation.opticalSize(24)],
  );

  static const heading = TextStyle(
    fontFamily: bodyFontFamily,
    fontWeight: FontWeight.w700,
    fontSize: 17,
    height: 24 / 17,
  );

  static const body = TextStyle(
    fontFamily: bodyFontFamily,
    fontWeight: FontWeight.w500,
    fontSize: 15,
    height: 22 / 15,
  );

  static const label = TextStyle(
    fontFamily: bodyFontFamily,
    fontWeight: FontWeight.w600,
    fontSize: 13,
    height: 18 / 13,
  );

  static const caption = TextStyle(
    fontFamily: bodyFontFamily,
    fontWeight: FontWeight.w600,
    fontSize: 11,
    height: 16 / 11,
  );
}
