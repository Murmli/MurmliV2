import 'dart:async';

import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/crash_reporting/crash_reporting_provider.dart';
import '../../../core/widgets/icon_tile.dart';
import 'settings_section.dart';

class CrashReportingSection extends ConsumerWidget {
  const CrashReportingSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.crashReporting;
    return SettingsSection(
      title: translations.section,
      children: [
        SwitchListTile(
          secondary: const IconTile(icon: Icons.bug_report_outlined),
          title: Text(translations.send),
          subtitle: Text(translations.description),
          value: ref.watch(crashReportingProvider),
          onChanged: (isEnabled) => unawaited(
            ref.read(crashReportingProvider.notifier).setEnabled(isEnabled),
          ),
        ),
      ],
    );
  }
}
