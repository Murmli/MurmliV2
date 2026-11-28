import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListPinnedHeader extends StatelessWidget {
  const ShoppingListPinnedHeader({
    required this.contentHeight,
    required this.child,
    super.key,
  });

  static const _gapBelow = AppSpacing.space4;

  final double contentHeight;
  final Widget child;

  static double lineHeightOf(BuildContext context, TextStyle style) =>
      MediaQuery.textScalerOf(context).scale(style.fontSize!) * style.height!;

  @override
  Widget build(BuildContext context) => SliverPersistentHeader(
    pinned: true,
    delegate: _PinnedHeaderDelegate(
      extent: contentHeight + _gapBelow,
      child: child,
    ),
  );
}

class _PinnedHeaderDelegate extends SliverPersistentHeaderDelegate {
  const _PinnedHeaderDelegate({required this.extent, required this.child});

  final double extent;
  final Widget child;

  @override
  double get minExtent => extent;

  @override
  double get maxExtent => extent;

  @override
  Widget build(
    BuildContext context,
    double shrinkOffset,
    bool overlapsContent,
  ) => SizedBox.expand(
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
  );

  @override
  bool shouldRebuild(_PinnedHeaderDelegate oldDelegate) =>
      extent != oldDelegate.extent || child != oldDelegate.child;
}
