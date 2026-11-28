import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/router/app_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class HomeHeader extends StatelessWidget {
  const HomeHeader({
    required this.title,
    this.trailing,
    this.padding = const EdgeInsetsDirectional.fromSTEB(
      AppSpacing.screenMargin,
      AppSpacing.space8,
      AppSpacing.space8,
      AppSpacing.space8,
    ),
    super.key,
  });

  final String title;
  final Widget? trailing;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: padding,
      child: Row(
        children: [
          Expanded(
            child: Text(
              title,
              style: TextTheme.of(context).titleLarge,
              overflow: TextOverflow.ellipsis,
            ),
          ),
          trailing ??
              IconButton(
                tooltip: context.t.home.settingsTooltip,
                icon: Icon(
                  Icons.settings_outlined,
                  color: context.colors.inkSecondary,
                ),
                onPressed: () =>
                    unawaited(context.router.push(const SettingsRoute())),
              ),
        ],
      ),
    );
  }
}
