import 'package:material_ui/material_ui.dart';

import 'app_colors.dart';
import 'app_typography.dart';

abstract final class AppTheme {
  static final light = _build(AppColors.light, Brightness.light);
  static final dark = _build(AppColors.dark, Brightness.dark);

  static ThemeData _build(AppColors colors, Brightness brightness) {
    final colorScheme = ColorScheme(
      brightness: brightness,
      primary: colors.primary,
      onPrimary: colors.onPrimary,
      primaryContainer: colors.primarySoft,
      onPrimaryContainer: colors.primaryText,
      secondary: colors.primaryText,
      onSecondary: colors.surface,
      secondaryContainer: colors.primarySoft,
      onSecondaryContainer: colors.primaryText,
      error: colors.categoryRed,
      onError: colors.surface,
      errorContainer: colors.categoryRedSoft,
      onErrorContainer: colors.categoryRed,
      surface: colors.surface,
      onSurface: colors.ink,
      onSurfaceVariant: colors.inkSecondary,
      surfaceContainerLowest: colors.background,
      surfaceContainerLow: colors.background,
      surfaceContainer: colors.surface,
      surfaceContainerHigh: colors.surfaceSecondary,
      surfaceContainerHighest: colors.surfaceSecondary,
      outline: colors.inkTertiary,
      outlineVariant: colors.line,
      shadow: Colors.black,
      inverseSurface: colors.ink,
      onInverseSurface: colors.background,
      inversePrimary: colors.primarySoft,
    );
    final textTheme = TextTheme(
      displaySmall: AppTypography.display,
      headlineSmall: AppTypography.title,
      titleLarge: AppTypography.title,
      titleMedium: AppTypography.heading,
      titleSmall: AppTypography.label,
      bodyLarge: AppTypography.body,
      bodyMedium: AppTypography.body,
      bodySmall: AppTypography.caption,
      labelLarge: AppTypography.label,
      labelMedium: AppTypography.label,
      labelSmall: AppTypography.caption,
    ).apply(bodyColor: colors.ink, displayColor: colors.ink);

    return ThemeData(
      brightness: brightness,
      colorScheme: colorScheme,
      scaffoldBackgroundColor: colors.background,
      fontFamily: AppTypography.bodyFontFamily,
      textTheme: textTheme,
      dividerColor: colors.line,
      appBarTheme: AppBarTheme(
        backgroundColor: colors.background,
        foregroundColor: colors.ink,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleTextStyle: AppTypography.title.copyWith(color: colors.ink),
      ),
      extensions: [colors],
    );
  }
}
