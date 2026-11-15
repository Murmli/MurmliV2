import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../app_language.dart';
import '../providers/app_language_provider.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class LanguageSection extends ConsumerWidget {
  const LanguageSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.language;
    final selectedLanguage = ref.watch(selectedAppLanguageProvider);
    return SettingsSection(
      title: translations.section,
      children: [
        for (final language in AppLanguage.values)
          SettingsRow(
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
