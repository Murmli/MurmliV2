import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/router/app_router.dart';
import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';
import '../../core/theme/app_typography.dart';
import '../../core/widgets/icon_tile.dart';
import '../onboarding/onboarding_completed_provider.dart';
import 'app_language.dart';
import 'app_language_provider.dart';
import 'app_version_provider.dart';
import 'theme_mode_provider.dart';

@RoutePage()
class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(context.t.settings.title)),
      body: ListView(
        padding: const EdgeInsetsDirectional.fromSTEB(
          AppSpacing.screenMargin,
          AppSpacing.space8,
          AppSpacing.screenMargin,
          AppSpacing.space32,
        ),
        children: const [
          _LanguageSection(),
          SizedBox(height: AppSpacing.space24),
          _ThemeSection(),
          SizedBox(height: AppSpacing.space24),
          _OnboardingSection(),
          SizedBox(height: AppSpacing.space24),
          _AboutSection(),
        ],
      ),
    );
  }
}

class _LanguageSection extends ConsumerWidget {
  const _LanguageSection();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.language;
    final selectedLanguage = ref.watch(selectedAppLanguageProvider);
    return _SettingsSection(
      title: translations.section,
      children: [
        for (final language in AppLanguage.values)
          _OptionRow(
            icon: switch (language) {
              AppLanguage.system => Icons.smartphone_outlined,
              AppLanguage.german || AppLanguage.english => Icons.translate,
            },
            label: switch (language) {
              AppLanguage.system => translations.system,
              AppLanguage.german => translations.german,
              AppLanguage.english => translations.english,
            },
            isSelected: language == selectedLanguage,
            onTap: () => unawaited(
              ref.read(selectedAppLanguageProvider.notifier).select(language),
            ),
          ),
      ],
    );
  }
}

class _ThemeSection extends ConsumerWidget {
  const _ThemeSection();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.theme;
    final selectedThemeMode = ref.watch(appThemeModeProvider);
    return _SettingsSection(
      title: translations.section,
      children: [
        for (final themeMode in ThemeMode.values)
          _OptionRow(
            icon: switch (themeMode) {
              ThemeMode.system => Icons.brightness_auto_outlined,
              ThemeMode.light => Icons.light_mode_outlined,
              ThemeMode.dark => Icons.dark_mode_outlined,
            },
            label: switch (themeMode) {
              ThemeMode.system => translations.system,
              ThemeMode.light => translations.light,
              ThemeMode.dark => translations.dark,
            },
            isSelected: themeMode == selectedThemeMode,
            onTap: () => unawaited(
              ref.read(appThemeModeProvider.notifier).select(themeMode),
            ),
          ),
      ],
    );
  }
}

class _OnboardingSection extends ConsumerWidget {
  const _OnboardingSection();

  Future<void> _showOnboardingAgain(BuildContext context, WidgetRef ref) async {
    await ref.read(onboardingCompletedProvider.notifier).reset();
    if (context.mounted) {
      await context.router.replaceAll([const OnboardingRoute()]);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.onboarding;
    return _SettingsSection(
      title: translations.section,
      children: [
        _SettingsRow(
          icon: Icons.replay,
          label: translations.showAgain,
          trailing: const _ChevronIcon(),
          onTap: () => unawaited(_showOnboardingAgain(context, ref)),
        ),
      ],
    );
  }
}

class _AboutSection extends StatelessWidget {
  const _AboutSection();

  @override
  Widget build(BuildContext context) {
    final translations = context.t.settings.about;
    return _SettingsSection(
      title: translations.section,
      children: [
        _SettingsRow(
          icon: Icons.info_outline,
          label: translations.version,
          trailing: const _AppVersionText(),
        ),
        _SettingsRow(
          icon: Icons.description_outlined,
          label: translations.licenses,
          trailing: const _ChevronIcon(),
          onTap: () => showLicensePage(context: context),
        ),
      ],
    );
  }
}

class _AppVersionText extends ConsumerWidget {
  const _AppVersionText();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    return switch (ref.watch(appVersionProvider)) {
      AsyncData(:final value) => Text(
        value,
        style: AppTypography.label.copyWith(color: colors.inkSecondary),
      ),
      AsyncError() => Text(
        context.t.settings.about.versionUnavailable,
        style: AppTypography.label.copyWith(color: colors.inkSecondary),
      ),
      _ => SizedBox.square(
        dimension: 16,
        child: CircularProgressIndicator(
          strokeWidth: 2,
          color: colors.inkTertiary,
        ),
      ),
    };
  }
}

class _SettingsSection extends StatelessWidget {
  const _SettingsSection({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      spacing: AppSpacing.space8,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.space4),
          child: Text(
            title,
            style: AppTypography.label.copyWith(color: colors.inkSecondary),
          ),
        ),
        DecoratedBox(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(AppRadius.card),
            boxShadow: colors.cardShadow,
          ),
          child: Material(
            color: colors.surface,
            borderRadius: BorderRadius.circular(AppRadius.card),
            clipBehavior: Clip.antiAlias,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.space8),
              child: Column(
                children: [
                  for (final (index, child) in children.indexed) ...[
                    if (index > 0)
                      Divider(
                        height: 1,
                        thickness: 1,
                        indent: AppSpacing.space16,
                        endIndent: AppSpacing.space16,
                        color: colors.line,
                      ),
                    child,
                  ],
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _OptionRow extends StatelessWidget {
  const _OptionRow({
    required this.icon,
    required this.label,
    required this.isSelected,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      selected: isSelected,
      child: _SettingsRow(
        icon: icon,
        label: label,
        trailing: isSelected
            ? Icon(Icons.check, color: context.colors.primaryText)
            : null,
        onTap: onTap,
      ),
    );
  }
}

class _SettingsRow extends StatelessWidget {
  const _SettingsRow({
    required this.icon,
    required this.label,
    this.trailing,
    this.onTap,
  });

  final IconData icon;
  final String label;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.space16),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 58),
          child: Row(
            spacing: AppSpacing.space12,
            children: [
              IconTile(icon: icon),
              Expanded(
                child: Text(
                  label,
                  style: AppTypography.body.copyWith(
                    fontWeight: FontWeight.w700,
                    color: context.colors.ink,
                  ),
                ),
              ),
              ?trailing,
            ],
          ),
        ),
      ),
    );
  }
}

class _ChevronIcon extends StatelessWidget {
  const _ChevronIcon();

  @override
  Widget build(BuildContext context) {
    return Icon(Icons.chevron_right, color: context.colors.inkTertiary);
  }
}
