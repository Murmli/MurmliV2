import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../providers/app_version_provider.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class AboutSection extends StatelessWidget {
  const AboutSection({super.key});

  @override
  Widget build(BuildContext context) {
    final translations = context.t.settings.about;
    return SettingsSection(
      title: translations.section,
      children: [
        SettingsRow(
          icon: Icons.info_outline,
          label: translations.version,
          trailing: const _AppVersionText(),
        ),
        SettingsRow(
          icon: Icons.description_outlined,
          label: translations.licenses,
          trailing: const Icon(Icons.chevron_right),
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
    return switch (ref.watch(appVersionProvider)) {
      AsyncData(:final value) => Text(value),
      AsyncError() => Text(context.t.settings.about.versionUnavailable),
      _ => const SizedBox.square(
        dimension: 16,
        child: CircularProgressIndicator(),
      ),
    };
  }
}
