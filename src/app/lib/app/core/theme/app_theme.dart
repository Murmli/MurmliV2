import 'package:material_ui/material_ui.dart';

import 'app_colors.dart';
import 'app_spacing.dart';
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
      displayMedium: AppTypography.hero,
      displaySmall: AppTypography.display,
      headlineSmall: AppTypography.title,
      titleLarge: AppTypography.title,
      titleMedium: AppTypography.heading,
      titleSmall: AppTypography.label,
      bodyLarge: AppTypography.bodyLarge,
      bodyMedium: AppTypography.body,
      bodySmall: AppTypography.caption.copyWith(fontWeight: FontWeight.w500),
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
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size.fromHeight(56),
          shape: const StadiumBorder(),
          textStyle: AppTypography.button,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size.fromHeight(56),
          shape: const StadiumBorder(),
          textStyle: AppTypography.button,
          foregroundColor: colors.ink,
          backgroundColor: colors.surface,
          side: BorderSide(color: colors.line),
        ),
      ),
      dividerTheme: DividerThemeData(
        color: colors.line,
        thickness: 1,
        space: 1,
      ),
      listTileTheme: ListTileThemeData(
        contentPadding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.space16,
        ),
        horizontalTitleGap: AppSpacing.space12,
        minTileHeight: 58,
        minVerticalPadding: 9,
        minLeadingWidth: 0,
        iconColor: colors.inkTertiary,
        selectedColor: colors.ink,
        titleTextStyle: AppTypography.body.copyWith(
          fontWeight: FontWeight.w700,
          color: colors.ink,
        ),
        subtitleTextStyle: AppTypography.label.copyWith(
          fontWeight: FontWeight.w500,
          color: colors.inkSecondary,
        ),
        leadingAndTrailingTextStyle: AppTypography.label.copyWith(
          color: colors.inkSecondary,
        ),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: colors.primarySoft,
        side: BorderSide.none,
        shape: const StadiumBorder(),
        labelStyle: AppTypography.label.copyWith(
          fontWeight: FontWeight.w700,
          color: colors.primaryText,
        ),
        iconTheme: IconThemeData(size: 18, color: colors.primaryText),
        padding: const EdgeInsetsDirectional.only(
          start: AppSpacing.space12,
          end: AppSpacing.space16,
        ),
        // Chip height is 32 plus the vertical label padding, giving 36.
        labelPadding: const EdgeInsetsDirectional.fromSTEB(
          AppSpacing.space8,
          2,
          0,
          2,
        ),
        avatarBoxConstraints: BoxConstraints.tight(const Size.square(18)),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: colors.inkTertiary,
        strokeWidth: 2,
      ),
      extensions: [colors],
    );
  }
}
