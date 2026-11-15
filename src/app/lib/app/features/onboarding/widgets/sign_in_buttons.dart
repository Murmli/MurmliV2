import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../providers/onboarding_completed_provider.dart';

class SignInButtons extends ConsumerWidget {
  const SignInButtons({super.key});

  Future<void> _completeOnboarding(BuildContext context, WidgetRef ref) async {
    await ref.read(onboardingCompletedProvider.notifier).complete();
    if (context.mounted) {
      await context.router.replaceAll([const HomeRoute()]);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final translations = context.t.onboarding;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      spacing: AppSpacing.space12,
      children: [
        FilledButton(
          onPressed: () => unawaited(_completeOnboarding(context, ref)),
          style: FilledButton.styleFrom(
            backgroundColor: colors.ink,
            foregroundColor: colors.background,
          ),
          child: Text(translations.continueWithApple),
        ),
        OutlinedButton(
          onPressed: () => unawaited(_completeOnboarding(context, ref)),
          child: Text(translations.continueWithGoogle),
        ),
      ],
    );
  }
}
