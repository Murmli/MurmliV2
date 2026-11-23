import 'package:material_ui/material_ui.dart';

import '../theme/app_colors.dart';
import '../theme/app_motion.dart';
import '../theme/app_spacing.dart';

enum ToastTone {
  info(Icons.info_outline_rounded, Duration(seconds: 4)),
  warning(Icons.schedule_rounded, Duration(seconds: 6)),
  error(Icons.error_outline_rounded, Duration(seconds: 4));

  const ToastTone(this.icon, this.duration);

  final IconData icon;
  final Duration duration;
}

void showToast(
  BuildContext context,
  String message, {
  ToastTone tone = ToastTone.info,
  IconData? icon,
}) {
  final animationDuration = AppMotion.short(context);
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(
        padding: EdgeInsets.zero,
        clipBehavior: Clip.none,
        duration: tone.duration,
        content: _ToastCard(
          message: message,
          tone: tone,
          icon: icon ?? tone.icon,
        ),
      ),
      snackBarAnimationStyle: animationDuration == Duration.zero
          ? AnimationStyle.noAnimation
          : AnimationStyle(
              duration: animationDuration,
              reverseDuration: animationDuration,
            ),
    );
}

class _ToastCard extends StatelessWidget {
  const _ToastCard({
    required this.message,
    required this.tone,
    required this.icon,
  });

  final String message;
  final ToastTone tone;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Container(
      padding: const EdgeInsetsDirectional.symmetric(
        horizontal: AppSpacing.space12,
        vertical: AppSpacing.space12,
      ),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppRadius.card),
        border: Border.all(color: colors.line),
        boxShadow: colors.navigationShadow,
      ),
      child: Row(
        spacing: AppSpacing.space12,
        children: [
          _ToastTile(tone: tone, icon: icon),
          Expanded(
            child: Text(
              message,
              style: TextTheme.of(context).bodyMedium
                  ?.copyWith(color: colors.ink),
            ),
          ),
        ],
      ),
    );
  }
}

class _ToastTile extends StatelessWidget {
  const _ToastTile({required this.tone, required this.icon});

  final ToastTone tone;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final (:accent, :soft) = switch (tone) {
      ToastTone.info => (accent: colors.primaryText, soft: colors.primarySoft),
      ToastTone.warning => (
        accent: colors.overTarget,
        soft: colors.categoryYellowSoft,
      ),
      ToastTone.error => (
        accent: colors.categoryRed,
        soft: colors.categoryRedSoft,
      ),
    };
    return Container(
      width: 40,
      height: 40,
      decoration: BoxDecoration(
        color: soft,
        borderRadius: BorderRadius.circular(AppRadius.iconTile),
      ),
      child: Icon(icon, size: 22, color: accent),
    );
  }
}
