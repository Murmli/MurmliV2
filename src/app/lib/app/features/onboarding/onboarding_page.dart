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
                    const SizedBox(height: 28),
                    Text(
                      translations.headline,
                      style: AppTypography.display.copyWith(
                        fontSize: 36,
                        height: 40 / 36,
                        letterSpacing: -0.72,
                        fontVariations: const [FontVariation.opticalSize(36)],
                        color: colors.ink,
                      ),
                    ),
                    const SizedBox(height: 14),
                    Text(
                      translations.subline,
                      style: AppTypography.body.copyWith(
                        fontSize: 16,
                        height: 24 / 16,
                        color: colors.inkSecondary,
                      ),
                    ),
                    const SizedBox(height: 28),
                    const _FeatureCard(),
                    const Spacer(),
                    const SizedBox(height: AppSpacing.space24),
                    const Center(child: _StartTokensChip()),
                    const SizedBox(height: 14),
                    const _SignInButtons(),
                    const SizedBox(height: 14),
                    Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: AppSpacing.space8,
                      ),
                      child: Text(
                        translations.legalNote,
                        textAlign: TextAlign.center,
                        style: AppTypography.caption.copyWith(
                          fontWeight: FontWeight.w500,
                          color: colors.inkSecondary,
                        ),
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
    final brandStyle = AppTypography.title.copyWith(
      fontSize: 26,
      height: 1,
      fontVariations: const [FontVariation.opticalSize(26)],
    );
    return Row(
      spacing: 10,
      children: [
        Container(
          width: 44,
          height: 44,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: colors.primary,
            borderRadius: BorderRadius.circular(15),
          ),
          child: Text(
            translations.logoInitial,
            style: brandStyle.copyWith(
              fontWeight: FontWeight.w800,
              color: colors.onPrimary,
            ),
          ),
        ),
        Text(
          translations.wordmark,
          style: brandStyle.copyWith(letterSpacing: -0.52, color: colors.ink),
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
    final divider = Divider(height: 1, thickness: 1, color: colors.line);
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.space16,
        vertical: AppSpacing.space8,
      ),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(28),
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
    final colors = context.colors;
    return ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 58),
      child: Row(
        spacing: 14,
        children: [
          IconTile(icon: icon),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  title,
                  style: AppTypography.body.copyWith(
                    fontWeight: FontWeight.w700,
                    color: colors.ink,
                  ),
                ),
                Text(
                  subtitle,
                  style: AppTypography.label.copyWith(
                    fontWeight: FontWeight.w500,
                    color: colors.inkSecondary,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _StartTokensChip extends StatelessWidget {
  const _StartTokensChip();

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Container(
      height: 36,
      padding: const EdgeInsets.only(left: 10, right: AppSpacing.space16),
      decoration: BoxDecoration(
        color: colors.primarySoft,
        borderRadius: BorderRadius.circular(18),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        spacing: AppSpacing.space8,
        children: [
          Icon(Icons.adjust, size: 18, color: colors.primaryText),
          Text(
            context.t.onboarding.startTokens,
            style: AppTypography.label.copyWith(
              fontWeight: FontWeight.w700,
              color: colors.primaryText,
            ),
          ),
        ],
      ),
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
      spacing: 10,
      children: [
        _PillButton(
          label: translations.continueWithApple,
          backgroundColor: colors.ink,
          foregroundColor: colors.background,
          onPressed: () => _completeOnboarding(context, ref),
        ),
        _PillButton(
          label: translations.continueWithGoogle,
          backgroundColor: colors.surface,
          foregroundColor: colors.ink,
          border: BorderSide(color: colors.line),
          onPressed: () => _completeOnboarding(context, ref),
        ),
      ],
    );
  }
}

class _PillButton extends StatelessWidget {
  const _PillButton({
    required this.label,
    required this.backgroundColor,
    required this.foregroundColor,
    required this.onPressed,
    this.border = BorderSide.none,
  });

  final String label;
  final Color backgroundColor;
  final Color foregroundColor;
  final BorderSide border;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    return FilledButton(
      onPressed: onPressed,
      style: FilledButton.styleFrom(
        backgroundColor: backgroundColor,
        foregroundColor: foregroundColor,
        minimumSize: const Size.fromHeight(56),
        shape: StadiumBorder(side: border),
        textStyle: AppTypography.body.copyWith(
          fontSize: 16,
          fontWeight: FontWeight.w700,
        ),
      ),
      child: Text(label),
    );
  }
}
