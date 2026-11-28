import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListPendingEntries extends StatelessWidget {
  const ShoppingListPendingEntries({required this.texts, super.key});

  final List<String> texts;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Padding(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: AppSpacing.space12,
      ),
      child: Container(
        padding: const EdgeInsetsDirectional.only(
          start: 6,
          end: AppSpacing.space16,
        ),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppRadius.listGroup),
          boxShadow: colors.cardShadow,
        ),
        child: Column(
          children: [
            for (final (index, text) in texts.indexed) ...[
              if (index > 0) const Divider(),
              _PendingEntryRow(text: text),
            ],
          ],
        ),
      ),
    );
  }
}

class _PendingEntryRow extends StatelessWidget {
  const _PendingEntryRow({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final translations = context.t.shoppingList.entry;
    return Semantics(
      liveRegion: true,
      label: translations.processing(text: text),
      excludeSemantics: true,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: Row(
          spacing: 10,
          children: [
            const SizedBox(width: 44, child: Center(child: _PendingTile())),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                spacing: 1,
                children: [
                  Text(
                    text,
                    style: textTheme.bodyMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                      height: 20 / 15,
                      color: colors.inkSecondary,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  Text(
                    translations.sorting,
                    style: textTheme.labelSmall?.copyWith(
                      fontWeight: FontWeight.w700,
                      height: 14 / 11,
                      color: colors.inkTertiary,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _PendingTile extends StatelessWidget {
  const _PendingTile();

  static const _size = 36.0;

  @override
  Widget build(BuildContext context) {
    final color = context.colors.inkTertiary;
    return CustomPaint(
      painter: _DashedRoundedSquarePainter(color: color),
      child: SizedBox.square(
        dimension: _size,
        child: Icon(Icons.auto_awesome_outlined, size: 18, color: color),
      ),
    );
  }
}

class _DashedRoundedSquarePainter extends CustomPainter {
  const _DashedRoundedSquarePainter({required this.color});

  static const _strokeWidth = 1.5;
  static const _radius = 12.0;
  static const _dashLength = 4.0;
  static const _gapLength = 3.0;

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final outline = Path()
      ..addRRect(
        RRect.fromRectAndRadius(
          (Offset.zero & size).deflate(_strokeWidth / 2),
          const Radius.circular(_radius - _strokeWidth / 2),
        ),
      );
    final dashes = Path();
    for (final metric in outline.computeMetrics()) {
      for (
        var distance = 0.0;
        distance < metric.length;
        distance += _dashLength + _gapLength
      ) {
        dashes.addPath(
          metric.extractPath(distance, distance + _dashLength),
          Offset.zero,
        );
      }
    }
    canvas.drawPath(
      dashes,
      Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = _strokeWidth,
    );
  }

  @override
  bool shouldRepaint(_DashedRoundedSquarePainter oldDelegate) =>
      color != oldDelegate.color;
}
