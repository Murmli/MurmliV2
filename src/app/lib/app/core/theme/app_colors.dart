import 'package:material_ui/material_ui.dart';

@immutable
class AppColors extends ThemeExtension<AppColors> {
  const AppColors({
    required this.background,
    required this.surface,
    required this.surfaceSecondary,
    required this.line,
    required this.ink,
    required this.inkSecondary,
    required this.inkTertiary,
    required this.primary,
    required this.onPrimary,
    required this.primarySoft,
    required this.primaryText,
    required this.protein,
    required this.proteinSoft,
    required this.carbohydrate,
    required this.carbohydrateSoft,
    required this.fat,
    required this.fatSoft,
    required this.onTarget,
    required this.overTarget,
    required this.categoryGreen,
    required this.categoryGreenSoft,
    required this.categoryYellow,
    required this.categoryYellowSoft,
    required this.categoryTeal,
    required this.categoryTealSoft,
    required this.categoryRed,
    required this.categoryRedSoft,
    required this.categoryBrown,
    required this.categoryBrownSoft,
    required this.categoryBlue,
    required this.categoryBlueSoft,
    required this.categoryCyan,
    required this.categoryCyanSoft,
    required this.categoryIndigo,
    required this.categoryIndigoSoft,
    required this.categoryPink,
    required this.categoryPinkSoft,
    required this.categorySlate,
    required this.categorySlateSoft,
    required this.categoryLime,
    required this.categoryLimeSoft,
    required this.categoryPurple,
    required this.categoryPurpleSoft,
    required this.cardShadow,
    required this.navigationShadow,
  });

  static const light = AppColors(
    background: Color(0xFFFFF9F1),
    surface: Color(0xFFFFFFFF),
    surfaceSecondary: Color(0xFFF6EFE5),
    line: Color(0xFFECE2D4),
    ink: Color(0xFF2B2118),
    inkSecondary: Color(0xFF66584B),
    inkTertiary: Color(0xFF766759),
    primary: Color(0xFFF57F17),
    onPrimary: Color(0xFF2B1500),
    primarySoft: Color(0xFFFFE8CF),
    primaryText: Color(0xFF9A4500),
    protein: Color(0xFF1E88E5),
    proteinSoft: Color(0xFFDCEBFB),
    carbohydrate: Color(0xFF43A047),
    carbohydrateSoft: Color(0xFFE1F2E2),
    fat: Color(0xFFFFB300),
    fatSoft: Color(0xFFFFF0C7),
    onTarget: Color(0xFF2E7D32),
    overTarget: Color(0xFFB45309),
    categoryGreen: Color(0xFF2E7D32),
    categoryGreenSoft: Color(0xFFE3F2E1),
    categoryYellow: Color(0xFF8A5E00),
    categoryYellowSoft: Color(0xFFFFF0C2),
    categoryTeal: Color(0xFF12766E),
    categoryTealSoft: Color(0xFFDAF0EC),
    categoryRed: Color(0xFFC0392B),
    categoryRedSoft: Color(0xFFFCE4DF),
    categoryBrown: Color(0xFF8A5426),
    categoryBrownSoft: Color(0xFFF0E4D8),
    categoryBlue: Color(0xFF1F62C4),
    categoryBlueSoft: Color(0xFFE1EBFB),
    categoryCyan: Color(0xFF0A7BA3),
    categoryCyanSoft: Color(0xFFDAF0F9),
    categoryIndigo: Color(0xFF4A43B5),
    categoryIndigoSoft: Color(0xFFE7E5FA),
    categoryPink: Color(0xFFB5306A),
    categoryPinkSoft: Color(0xFFFBE3ED),
    categorySlate: Color(0xFF565B6B),
    categorySlateSoft: Color(0xFFE8E9ED),
    categoryLime: Color(0xFF5A7410),
    categoryLimeSoft: Color(0xFFECF2D0),
    categoryPurple: Color(0xFF7E36A8),
    categoryPurpleSoft: Color(0xFFEFE3F8),
    cardShadow: [
      BoxShadow(color: Color(0x0D3C230A), offset: Offset(0, 1), blurRadius: 2),
      BoxShadow(
        color: Color(0x293C230A),
        offset: Offset(0, 10),
        blurRadius: 24,
        spreadRadius: -12,
      ),
    ],
    navigationShadow: [
      BoxShadow(
        color: Color(0x423C230A),
        offset: Offset(0, 12),
        blurRadius: 32,
        spreadRadius: -8,
      ),
    ],
  );

  static const dark = AppColors(
    background: Color(0xFF17110C),
    surface: Color(0xFF221A13),
    surfaceSecondary: Color(0xFF2E241B),
    line: Color(0xFF3A2E23),
    ink: Color(0xFFF7EEE3),
    inkSecondary: Color(0xFFC9BAA9),
    inkTertiary: Color(0xFFA39382),
    primary: Color(0xFFFF9A3D),
    onPrimary: Color(0xFF2B1500),
    primarySoft: Color(0xFF3B2410),
    primaryText: Color(0xFFFFB366),
    protein: Color(0xFF5AA9F0),
    proteinSoft: Color(0xFF14283E),
    carbohydrate: Color(0xFF66BB6A),
    carbohydrateSoft: Color(0xFF1C3320),
    fat: Color(0xFFFFC947),
    fatSoft: Color(0xFF3A2E10),
    onTarget: Color(0xFF7BD17F),
    overTarget: Color(0xFFFFB366),
    categoryGreen: Color(0xFF7BD07F),
    categoryGreenSoft: Color(0xFF1F3320),
    categoryYellow: Color(0xFFF2C94C),
    categoryYellowSoft: Color(0xFF3A2E10),
    categoryTeal: Color(0xFF5CCBC0),
    categoryTealSoft: Color(0xFF17332F),
    categoryRed: Color(0xFFFF8A78),
    categoryRedSoft: Color(0xFF3E211D),
    categoryBrown: Color(0xFFD9A06B),
    categoryBrownSoft: Color(0xFF36271B),
    categoryBlue: Color(0xFF7FAEF5),
    categoryBlueSoft: Color(0xFF1A2A45),
    categoryCyan: Color(0xFF5CC6EC),
    categoryCyanSoft: Color(0xFF14313D),
    categoryIndigo: Color(0xFFA9A3F5),
    categoryIndigoSoft: Color(0xFF26234A),
    categoryPink: Color(0xFFF58AB5),
    categoryPinkSoft: Color(0xFF3F2030),
    categorySlate: Color(0xFFA9AEBD),
    categorySlateSoft: Color(0xFF2A2C33),
    categoryLime: Color(0xFFB5D35A),
    categoryLimeSoft: Color(0xFF2B3314),
    categoryPurple: Color(0xFFCF9AEF),
    categoryPurpleSoft: Color(0xFF35213F),
    cardShadow: [
      BoxShadow(color: Color(0x4D000000), offset: Offset(0, 1), blurRadius: 2),
      BoxShadow(
        color: Color(0x99000000),
        offset: Offset(0, 10),
        blurRadius: 24,
        spreadRadius: -12,
      ),
    ],
    navigationShadow: [
      BoxShadow(
        color: Color(0xB3000000),
        offset: Offset(0, 12),
        blurRadius: 32,
        spreadRadius: -8,
      ),
    ],
  );

  final Color background;
  final Color surface;
  final Color surfaceSecondary;
  final Color line;
  final Color ink;
  final Color inkSecondary;
  final Color inkTertiary;
  final Color primary;
  final Color onPrimary;
  final Color primarySoft;
  final Color primaryText;
  final Color protein;
  final Color proteinSoft;
  final Color carbohydrate;
  final Color carbohydrateSoft;
  final Color fat;
  final Color fatSoft;
  final Color onTarget;
  final Color overTarget;
  final Color categoryGreen;
  final Color categoryGreenSoft;
  final Color categoryYellow;
  final Color categoryYellowSoft;
  final Color categoryTeal;
  final Color categoryTealSoft;
  final Color categoryRed;
  final Color categoryRedSoft;
  final Color categoryBrown;
  final Color categoryBrownSoft;
  final Color categoryBlue;
  final Color categoryBlueSoft;
  final Color categoryCyan;
  final Color categoryCyanSoft;
  final Color categoryIndigo;
  final Color categoryIndigoSoft;
  final Color categoryPink;
  final Color categoryPinkSoft;
  final Color categorySlate;
  final Color categorySlateSoft;
  final Color categoryLime;
  final Color categoryLimeSoft;
  final Color categoryPurple;
  final Color categoryPurpleSoft;
  final List<BoxShadow> cardShadow;
  final List<BoxShadow> navigationShadow;

  @override
  AppColors copyWith({
    Color? background,
    Color? surface,
    Color? surfaceSecondary,
    Color? line,
    Color? ink,
    Color? inkSecondary,
    Color? inkTertiary,
    Color? primary,
    Color? onPrimary,
    Color? primarySoft,
    Color? primaryText,
    Color? protein,
    Color? proteinSoft,
    Color? carbohydrate,
    Color? carbohydrateSoft,
    Color? fat,
    Color? fatSoft,
    Color? onTarget,
    Color? overTarget,
    Color? categoryGreen,
    Color? categoryGreenSoft,
    Color? categoryYellow,
    Color? categoryYellowSoft,
    Color? categoryTeal,
    Color? categoryTealSoft,
    Color? categoryRed,
    Color? categoryRedSoft,
    Color? categoryBrown,
    Color? categoryBrownSoft,
    Color? categoryBlue,
    Color? categoryBlueSoft,
    Color? categoryCyan,
    Color? categoryCyanSoft,
    Color? categoryIndigo,
    Color? categoryIndigoSoft,
    Color? categoryPink,
    Color? categoryPinkSoft,
    Color? categorySlate,
    Color? categorySlateSoft,
    Color? categoryLime,
    Color? categoryLimeSoft,
    Color? categoryPurple,
    Color? categoryPurpleSoft,
    List<BoxShadow>? cardShadow,
    List<BoxShadow>? navigationShadow,
  }) {
    return AppColors(
      background: background ?? this.background,
      surface: surface ?? this.surface,
      surfaceSecondary: surfaceSecondary ?? this.surfaceSecondary,
      line: line ?? this.line,
      ink: ink ?? this.ink,
      inkSecondary: inkSecondary ?? this.inkSecondary,
      inkTertiary: inkTertiary ?? this.inkTertiary,
      primary: primary ?? this.primary,
      onPrimary: onPrimary ?? this.onPrimary,
      primarySoft: primarySoft ?? this.primarySoft,
      primaryText: primaryText ?? this.primaryText,
      protein: protein ?? this.protein,
      proteinSoft: proteinSoft ?? this.proteinSoft,
      carbohydrate: carbohydrate ?? this.carbohydrate,
      carbohydrateSoft: carbohydrateSoft ?? this.carbohydrateSoft,
      fat: fat ?? this.fat,
      fatSoft: fatSoft ?? this.fatSoft,
      onTarget: onTarget ?? this.onTarget,
      overTarget: overTarget ?? this.overTarget,
      categoryGreen: categoryGreen ?? this.categoryGreen,
      categoryGreenSoft: categoryGreenSoft ?? this.categoryGreenSoft,
      categoryYellow: categoryYellow ?? this.categoryYellow,
      categoryYellowSoft: categoryYellowSoft ?? this.categoryYellowSoft,
      categoryTeal: categoryTeal ?? this.categoryTeal,
      categoryTealSoft: categoryTealSoft ?? this.categoryTealSoft,
      categoryRed: categoryRed ?? this.categoryRed,
      categoryRedSoft: categoryRedSoft ?? this.categoryRedSoft,
      categoryBrown: categoryBrown ?? this.categoryBrown,
      categoryBrownSoft: categoryBrownSoft ?? this.categoryBrownSoft,
      categoryBlue: categoryBlue ?? this.categoryBlue,
      categoryBlueSoft: categoryBlueSoft ?? this.categoryBlueSoft,
      categoryCyan: categoryCyan ?? this.categoryCyan,
      categoryCyanSoft: categoryCyanSoft ?? this.categoryCyanSoft,
      categoryIndigo: categoryIndigo ?? this.categoryIndigo,
      categoryIndigoSoft: categoryIndigoSoft ?? this.categoryIndigoSoft,
      categoryPink: categoryPink ?? this.categoryPink,
      categoryPinkSoft: categoryPinkSoft ?? this.categoryPinkSoft,
      categorySlate: categorySlate ?? this.categorySlate,
      categorySlateSoft: categorySlateSoft ?? this.categorySlateSoft,
      categoryLime: categoryLime ?? this.categoryLime,
      categoryLimeSoft: categoryLimeSoft ?? this.categoryLimeSoft,
      categoryPurple: categoryPurple ?? this.categoryPurple,
      categoryPurpleSoft: categoryPurpleSoft ?? this.categoryPurpleSoft,
      cardShadow: cardShadow ?? this.cardShadow,
      navigationShadow: navigationShadow ?? this.navigationShadow,
    );
  }

  @override
  AppColors lerp(AppColors? other, double t) {
    if (other == null) {
      return this;
    }
    return AppColors(
      background: Color.lerp(background, other.background, t)!,
      surface: Color.lerp(surface, other.surface, t)!,
      surfaceSecondary: Color.lerp(
        surfaceSecondary,
        other.surfaceSecondary,
        t,
      )!,
      line: Color.lerp(line, other.line, t)!,
      ink: Color.lerp(ink, other.ink, t)!,
      inkSecondary: Color.lerp(inkSecondary, other.inkSecondary, t)!,
      inkTertiary: Color.lerp(inkTertiary, other.inkTertiary, t)!,
      primary: Color.lerp(primary, other.primary, t)!,
      onPrimary: Color.lerp(onPrimary, other.onPrimary, t)!,
      primarySoft: Color.lerp(primarySoft, other.primarySoft, t)!,
      primaryText: Color.lerp(primaryText, other.primaryText, t)!,
      protein: Color.lerp(protein, other.protein, t)!,
      proteinSoft: Color.lerp(proteinSoft, other.proteinSoft, t)!,
      carbohydrate: Color.lerp(carbohydrate, other.carbohydrate, t)!,
      carbohydrateSoft: Color.lerp(
        carbohydrateSoft,
        other.carbohydrateSoft,
        t,
      )!,
      fat: Color.lerp(fat, other.fat, t)!,
      fatSoft: Color.lerp(fatSoft, other.fatSoft, t)!,
      onTarget: Color.lerp(onTarget, other.onTarget, t)!,
      overTarget: Color.lerp(overTarget, other.overTarget, t)!,
      categoryGreen: Color.lerp(categoryGreen, other.categoryGreen, t)!,
      categoryGreenSoft: Color.lerp(
        categoryGreenSoft,
        other.categoryGreenSoft,
        t,
      )!,
      categoryYellow: Color.lerp(categoryYellow, other.categoryYellow, t)!,
      categoryYellowSoft: Color.lerp(
        categoryYellowSoft,
        other.categoryYellowSoft,
        t,
      )!,
      categoryTeal: Color.lerp(categoryTeal, other.categoryTeal, t)!,
      categoryTealSoft: Color.lerp(
        categoryTealSoft,
        other.categoryTealSoft,
        t,
      )!,
      categoryRed: Color.lerp(categoryRed, other.categoryRed, t)!,
      categoryRedSoft: Color.lerp(categoryRedSoft, other.categoryRedSoft, t)!,
      categoryBrown: Color.lerp(categoryBrown, other.categoryBrown, t)!,
      categoryBrownSoft: Color.lerp(
        categoryBrownSoft,
        other.categoryBrownSoft,
        t,
      )!,
      categoryBlue: Color.lerp(categoryBlue, other.categoryBlue, t)!,
      categoryBlueSoft: Color.lerp(
        categoryBlueSoft,
        other.categoryBlueSoft,
        t,
      )!,
      categoryCyan: Color.lerp(categoryCyan, other.categoryCyan, t)!,
      categoryCyanSoft: Color.lerp(
        categoryCyanSoft,
        other.categoryCyanSoft,
        t,
      )!,
      categoryIndigo: Color.lerp(categoryIndigo, other.categoryIndigo, t)!,
      categoryIndigoSoft: Color.lerp(
        categoryIndigoSoft,
        other.categoryIndigoSoft,
        t,
      )!,
      categoryPink: Color.lerp(categoryPink, other.categoryPink, t)!,
      categoryPinkSoft: Color.lerp(
        categoryPinkSoft,
        other.categoryPinkSoft,
        t,
      )!,
      categorySlate: Color.lerp(categorySlate, other.categorySlate, t)!,
      categorySlateSoft: Color.lerp(
        categorySlateSoft,
        other.categorySlateSoft,
        t,
      )!,
      categoryLime: Color.lerp(categoryLime, other.categoryLime, t)!,
      categoryLimeSoft: Color.lerp(
        categoryLimeSoft,
        other.categoryLimeSoft,
        t,
      )!,
      categoryPurple: Color.lerp(categoryPurple, other.categoryPurple, t)!,
      categoryPurpleSoft: Color.lerp(
        categoryPurpleSoft,
        other.categoryPurpleSoft,
        t,
      )!,
      cardShadow: BoxShadow.lerpList(cardShadow, other.cardShadow, t)!,
      navigationShadow: BoxShadow.lerpList(
        navigationShadow,
        other.navigationShadow,
        t,
      )!,
    );
  }
}

extension AppColorsContext on BuildContext {
  AppColors get colors => Theme.of(this).extension<AppColors>()!;
}
