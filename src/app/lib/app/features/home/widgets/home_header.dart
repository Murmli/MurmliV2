import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class HomeHeader extends StatelessWidget {
  const HomeHeader({required this.title, this.titleTrailing, super.key});

  final String title;
  final Widget? titleTrailing;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final titleTrailing = this.titleTrailing;
    return Padding(
      padding: const EdgeInsetsDirectional.fromSTEB(
        AppSpacing.screenMargin,
        AppSpacing.space8,
        AppSpacing.space8,
        AppSpacing.space8,
      ),
      child: Row(
        children: [
          Flexible(
            child: Text(
              title,
              style: TextTheme.of(context).titleLarge,
              overflow: TextOverflow.ellipsis,
            ),
          ),
          if (titleTrailing != null) ...[
            const SizedBox(width: AppSpacing.space8),
            titleTrailing,
          ],
          const Spacer(),
          IconButton(
            tooltip: context.t.home.settingsTooltip,
            icon: Icon(Icons.settings_outlined, color: colors.inkSecondary),
            onPressed: () =>
                unawaited(context.router.push(const SettingsRoute())),
          ),
        ],
      ),
    );
  }
}
