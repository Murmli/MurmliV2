import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../providers/theme_mode_provider.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class ThemeSection extends ConsumerWidget {
  const ThemeSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.theme;
    final selectedThemeMode = ref.watch(appThemeModeProvider);
    return SettingsSection(
      title: translations.section,
      children: [
        for (final themeMode in ThemeMode.values)
          SettingsRow(
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
