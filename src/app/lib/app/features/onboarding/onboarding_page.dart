import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';
import 'widgets/brand.dart';
import 'widgets/feature_card.dart';
import 'widgets/sign_in_options.dart';

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
                    const Brand(),
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
                    const FeatureCard(),
                    const Spacer(),
                    const SizedBox(height: AppSpacing.space24),
                    Center(
                      child: Chip(
                        avatar: const Icon(Icons.adjust),
                        label: Text(translations.startTokens),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.space16),
                    const SignInOptions(),
                    const SizedBox(height: AppSpacing.space16),
                    Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: AppSpacing.space8,
                      ),
                      child: DefaultTextStyle.merge(
                        textAlign: TextAlign.center,
                        style: TextTheme.of(context).bodySmall
                            ?.copyWith(color: colors.inkSecondary),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          spacing: AppSpacing.space8,
                          children: [
                            Text(translations.legalNote),
                            Text(translations.crashReportsNote),
                          ],
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
