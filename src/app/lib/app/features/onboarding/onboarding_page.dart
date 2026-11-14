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
import 'onboarding_completed_provider.dart';

@RoutePage()
class OnboardingPage extends StatelessWidget {
  const OnboardingPage({super.key});

  @override
  Widget build(BuildContext context) {
    final translations = context.t.onboarding;
    final colors = context.colors;
    return Scaffold(
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, constraints) => SingleChildScrollView(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.space24,
              vertical: AppSpacing.space16,
            ),
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight: constraints.maxHeight - 2 * AppSpacing.space16,
              ),
              child: IntrinsicHeight(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const _Brand(),
                    const SizedBox(height: AppSpacing.space32),
                    Text(
                      translations.headline,
                      style: TextTheme.of(context).displayMedium,
                    ),
                    const SizedBox(height: AppSpacing.space16),
                    Text(
                      translations.subline,
                      style: TextTheme.of(context).bodyLarge
                          ?.copyWith(color: colors.inkSecondary),
                    ),
                    const SizedBox(height: AppSpacing.space32),
                    const _FeatureCard(),
                    const Spacer(),
                    const SizedBox(height: AppSpacing.space24),
                    Center(
                      child: Chip(
                        avatar: const Icon(Icons.adjust),
                        label: Text(translations.startTokens),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.space16),
                    const _SignInButtons(),
                    const SizedBox(height: AppSpacing.space16),
                    Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: AppSpacing.space8,
                      ),
                      child: Text(
                        translations.legalNote,
                        textAlign: TextAlign.center,
                        style: TextTheme.of(context).bodySmall
                            ?.copyWith(color: colors.inkSecondary),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Brand extends StatelessWidget {
  const _Brand();

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final translations = context.t.app;
    return Row(
      spacing: AppSpacing.space12,
      children: [
        Container(
          width: 44,
          height: 44,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: colors.primary,
            borderRadius: BorderRadius.circular(AppRadius.brandMark),
          ),
          child: Text(
            translations.logoInitial,
            style: AppTypography.brand.copyWith(
              fontWeight: FontWeight.w800,
              color: colors.onPrimary,
            ),
          ),
        ),
        Text(
          translations.wordmark,
          style: AppTypography.wordmark.copyWith(color: colors.ink),
        ),
      ],
    );
  }
}

class _FeatureCard extends StatelessWidget {
  const _FeatureCard();

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final features = context.t.onboarding.features;
    const divider = Divider(
      indent: AppSpacing.space16,
      endIndent: AppSpacing.space16,
    );
    return Container(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.space8),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppRadius.featureCard),
        boxShadow: colors.cardShadow,
      ),
      child: Column(
        children: [
          _FeatureRow(
            icon: Icons.auto_awesome,
            title: features.assistant.title,
            subtitle: features.assistant.subtitle,
          ),
          divider,
          _FeatureRow(
            icon: Icons.local_fire_department_outlined,
            title: features.calories.title,
            subtitle: features.calories.subtitle,
          ),
          divider,
          _FeatureRow(
            icon: Icons.shopping_cart_outlined,
            title: features.shopping.title,
            subtitle: features.shopping.subtitle,
          ),
          divider,
          _FeatureRow(
            icon: Icons.calendar_today_outlined,
            title: features.planning.title,
            subtitle: features.planning.subtitle,
          ),
        ],
      ),
    );
  }
}

class _FeatureRow extends StatelessWidget {
  const _FeatureRow({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      leading: IconTile(icon: icon),
      title: Text(title),
      subtitle: Text(subtitle),
    );
  }
}

class _SignInButtons extends ConsumerWidget {
  const _SignInButtons();

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
