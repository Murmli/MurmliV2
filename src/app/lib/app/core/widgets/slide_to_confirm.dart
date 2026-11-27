import 'dart:async';

import 'package:flutter/services.dart';
import 'package:material_ui/material_ui.dart';

import '../theme/app_motion.dart';
import '../theme/app_spacing.dart';

class SlideToConfirm extends StatefulWidget {
  const SlideToConfirm({
    required this.label,
    required this.onConfirmed,
    super.key,
  });

  final String label;
  final VoidCallback onConfirmed;

  @override
  State<SlideToConfirm> createState() => _SlideToConfirmState();
}

class _SlideToConfirmState extends State<SlideToConfirm>
    with SingleTickerProviderStateMixin {
  static const _thumbSize = 48.0;
  static const _thumbInset = AppSpacing.space4;

  late final _progress = AnimationController(vsync: this);
  var _isConfirmed = false;

  @override
  void dispose() {
    _progress.dispose();
    super.dispose();
  }

  void _confirm() {
    if (_isConfirmed) {
      return;
    }
    _isConfirmed = true;
    _progress.value = 1;
    unawaited(HapticFeedback.mediumImpact());
    widget.onConfirmed();
  }

  void _drag(DragUpdateDetails details) {
    if (_isConfirmed) {
      return;
    }
    final travel = context.size!.width - _thumbSize - 2 * _thumbInset;
    final direction = Directionality.of(context) == TextDirection.rtl ? -1 : 1;
    _progress.value += direction * details.primaryDelta! / travel;
    if (_progress.value >= 1) {
      _confirm();
    }
  }

  void _release() {
    if (_isConfirmed) {
      return;
    }
    unawaited(
      _progress.animateTo(
        0,
        duration: AppMotion.short(context),
        curve: Curves.easeOut,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = ColorScheme.of(context);
    return Semantics(
      button: true,
      label: widget.label,
      onTap: _confirm,
      excludeSemantics: true,
      child: GestureDetector(
        onHorizontalDragUpdate: _drag,
        onHorizontalDragEnd: (_) => _release(),
        onHorizontalDragCancel: _release,
        child: DecoratedBox(
          decoration: ShapeDecoration(
            color: colorScheme.errorContainer,
            shape: const StadiumBorder(),
          ),
          child: Stack(
            children: [
              Positioned.fill(
                child: CustomPaint(
                  painter: _TrailPainter(
                    progress: _progress,
                    color: colorScheme.error.withValues(alpha: 0.35),
                    leadingLength: _thumbSize + 2 * _thumbInset,
                    textDirection: Directionality.of(context),
                  ),
                ),
              ),
              Padding(
                padding: const EdgeInsetsDirectional.all(_thumbInset),
                child: Stack(
                  alignment: AlignmentDirectional.center,
                  children: [
                    Padding(
                      padding: const EdgeInsetsDirectional.symmetric(
                        horizontal: _thumbSize,
                      ),
                      child: Text(
                        widget.label,
                        textAlign: TextAlign.center,
                        style: TextTheme.of(context).labelLarge
                            ?.copyWith(color: colorScheme.onErrorContainer),
                      ),
                    ),
                    AnimatedBuilder(
                      animation: _progress,
                      builder: (context, child) => Align(
                        alignment: AlignmentDirectional(
                          2 * _progress.value - 1,
                          0,
                        ),
                        child: child,
                      ),
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          color: colorScheme.error,
                          shape: BoxShape.circle,
                        ),
                        child: SizedBox.square(
                          dimension: _thumbSize,
                          child: Icon(
                            Icons.arrow_forward,
                            color: colorScheme.onError,
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _TrailPainter extends CustomPainter {
  _TrailPainter({
    required this.progress,
    required this.color,
    required this.leadingLength,
    required this.textDirection,
  }) : super(repaint: progress);

  final Animation<double> progress;
  final Color color;
  final double leadingLength;
  final TextDirection textDirection;

  @override
  void paint(Canvas canvas, Size size) {
    final width = leadingLength + progress.value * (size.width - leadingLength);
    final left = textDirection == TextDirection.rtl ? size.width - width : 0.0;
    canvas.drawRRect(
      RRect.fromRectAndRadius(
        Rect.fromLTWH(left, 0, width, size.height),
        Radius.circular(size.height / 2),
      ),
      Paint()..color = color,
    );
  }

  @override
  bool shouldRepaint(_TrailPainter oldDelegate) =>
      color != oldDelegate.color ||
      leadingLength != oldDelegate.leadingLength ||
      textDirection != oldDelegate.textDirection ||
      progress != oldDelegate.progress;
}
