import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListPinnedHeader extends StatelessWidget {
  const ShoppingListPinnedHeader({
    required this.contentHeight,
    required this.visibleFraction,
    required this.child,
    super.key,
  });

  static const _gapBelow = AppSpacing.space4;

  final double contentHeight;
  final double visibleFraction;
  final Widget child;

  static TextStyle titleStyleOf(BuildContext context) => TextTheme.of(context)
      .titleSmall!
      .copyWith(
        fontWeight: FontWeight.w700,
        height: 20 / 13,
        color: context.colors.inkSecondary,
      );

  static double lineHeightOf(BuildContext context, TextStyle style) =>
      MediaQuery.textScalerOf(context).scale(style.fontSize!) * style.height!;

  @override
  Widget build(BuildContext context) => SliverPersistentHeader(
    pinned: true,
    delegate: _PinnedHeaderDelegate(
      fullExtent: contentHeight + _gapBelow,
      visibleFraction: visibleFraction,
      child: child,
    ),
  );
}

class _PinnedHeaderDelegate extends SliverPersistentHeaderDelegate {
  const _PinnedHeaderDelegate({
    required this.fullExtent,
    required this.visibleFraction,
    required this.child,
  });

  final double fullExtent;
  final double visibleFraction;
  final Widget child;

  double get _extent => fullExtent * visibleFraction;

  @override
  double get minExtent => _extent;

  @override
  double get maxExtent => _extent;

  // While the section collapses or expands, the header keeps its full layout and
  // shows only its top part.
  @override
  Widget build(
    BuildContext context,
    double shrinkOffset,
    bool overlapsContent,
  ) => ClipRect(
    child: OverflowBox(
      alignment: AlignmentDirectional.topStart,
      minHeight: fullExtent,
      maxHeight: fullExtent,
      child: Material(
        color: context.colors.background,
        child: Padding(
          padding: const EdgeInsetsDirectional.only(
            start: AppSpacing.screenMargin,
            end: AppSpacing.screenMargin,
            bottom: ShoppingListPinnedHeader._gapBelow,
          ),
          child: child,
        ),
      ),
    ),
  );

  @override
  bool shouldRebuild(_PinnedHeaderDelegate oldDelegate) =>
      fullExtent != oldDelegate.fullExtent ||
      visibleFraction != oldDelegate.visibleFraction ||
      child != oldDelegate.child;
}
