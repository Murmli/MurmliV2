import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';
import '../../onboarding/providers/onboarding_completed_provider.dart';
import 'settings_row.dart';
import 'settings_section.dart';

class OnboardingSection extends ConsumerWidget {
  const OnboardingSection({super.key});

  Future<void> _showOnboardingAgain(BuildContext context, WidgetRef ref) async {
    await ref.read(onboardingCompletedProvider.notifier).reset();
    if (context.mounted) {
      await context.router.replaceAll([const OnboardingRoute()]);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.settings.onboarding;
    return SettingsSection(
      title: translations.section,
      children: [
        SettingsRow(
          icon: Icons.replay,
          label: translations.showAgain,
          trailing: const Icon(Icons.chevron_right),
          onTap: () => unawaited(_showOnboardingAgain(context, ref)),
        ),
      ],
    );
  }
}
