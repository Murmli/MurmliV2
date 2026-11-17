import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../i18n/strings.g.dart';
import '../../core/theme/app_spacing.dart';
import 'widgets/about_section.dart';
import 'widgets/crash_reporting_section.dart';
import 'widgets/language_section.dart';
import 'widgets/onboarding_section.dart';
import 'widgets/theme_section.dart';

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
          LanguageSection(),
          SizedBox(height: AppSpacing.space24),
          ThemeSection(),
          SizedBox(height: AppSpacing.space24),
          OnboardingSection(),
          SizedBox(height: AppSpacing.space24),
          CrashReportingSection(),
          SizedBox(height: AppSpacing.space24),
          AboutSection(),
        ],
      ),
    );
  }
}
