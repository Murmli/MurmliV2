import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';

const _maxLabelWidth = 80.0;

class ShoppingListProgressCounter extends StatelessWidget {
  const ShoppingListProgressCounter({
    required this.checkedCount,
    required this.totalCount,
    super.key,
  });

  final int checkedCount;
  final int totalCount;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final style = TextTheme.of(context).labelLarge
        ?.copyWith(color: colors.inkSecondary);
    return Semantics(
      label: context.t.shoppingList.progress(
        checked: checkedCount,
        total: totalCount,
      ),
      excludeSemantics: true,
      child: TweenAnimationBuilder<double>(
        tween: Tween(end: checkedCount / totalCount),
        duration: AppMotion.short(context),
        builder: (context, progress, child) => CustomPaint(
          foregroundPainter: _ProgressOutlinePainter(
            progress: progress,
            trackColor: colors.line,
            progressColor: colors.onTarget,
          ),
          child: child,
        ),
        child: DecoratedBox(
          decoration: ShapeDecoration(
            color: colors.surfaceSecondary,
            shape: const StadiumBorder(),
          ),
          child: Padding(
            padding: const EdgeInsetsDirectional.symmetric(
              horizontal: AppSpacing.space8,
              vertical: 2,
            ),
            child: Text(_label(context, style), style: style),
          ),
        ),
      ),
    );
  }

  String _label(BuildContext context, TextStyle? style) {
    final spacedLabel = '$checkedCount / $totalCount';
    final painter = TextPainter(
      text: TextSpan(text: spacedLabel, style: style),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();
    final fits = painter.width <= _maxLabelWidth;
    painter.dispose();
    return fits ? spacedLabel : '$checkedCount/$totalCount';
  }
}

class _ProgressOutlinePainter extends CustomPainter {
  const _ProgressOutlinePainter({
    required this.progress,
    required this.trackColor,
    required this.progressColor,
  });

  static const _strokeWidth = 2.5;

  final double progress;
  final Color trackColor;
  final Color progressColor;

  @override
  void paint(Canvas canvas, Size size) {
    final outline = _stadiumFromTopCenter(
      (Offset.zero & size).deflate(_strokeWidth / 2),
    );
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = _strokeWidth
      ..color = trackColor;
    canvas.drawPath(outline, paint);
    if (progress <= 0) {
      return;
    }
    final metric = outline.computeMetrics().single;
    canvas.drawPath(
      metric.extractPath(0, progress * metric.length),
      paint
        ..color = progressColor
        ..strokeCap = StrokeCap.round,
    );
  }

  Path _stadiumFromTopCenter(Rect bounds) {
    final radius = Radius.circular(bounds.shortestSide / 2);
    return Path()
      ..moveTo(bounds.center.dx, bounds.top)
      ..lineTo(bounds.right - radius.x, bounds.top)
      ..arcToPoint(
        Offset(bounds.right - radius.x, bounds.bottom),
        radius: radius,
      )
      ..lineTo(bounds.left + radius.x, bounds.bottom)
      ..arcToPoint(Offset(bounds.left + radius.x, bounds.top), radius: radius)
      ..close();
  }

  @override
  bool shouldRepaint(_ProgressOutlinePainter oldDelegate) =>
      progress != oldDelegate.progress ||
      trackColor != oldDelegate.trackColor ||
      progressColor != oldDelegate.progressColor;
}
