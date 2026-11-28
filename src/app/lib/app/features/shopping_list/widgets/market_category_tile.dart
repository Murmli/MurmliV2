import 'package:flutter_svg/flutter_svg.dart';
import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_colors.dart';
import '../market_category_appearance.dart';
import '../models/market_category.dart';

class MarketCategoryTile extends StatelessWidget {
  const MarketCategoryTile({required this.category, this.size = 36, super.key});

  final MarketCategory category;
  final double size;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    final colorFamily = category.colorFamily;
    final iconSize = size * 0.55;
    return ExcludeSemantics(
      child: Container(
        width: size,
        height: size,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colorFamily.background(colors),
          borderRadius: BorderRadius.circular(size * 0.32),
        ),
        child: SvgPicture.asset(
          category.iconAsset,
          width: iconSize,
          height: iconSize,
          colorFilter: ColorFilter.mode(
            colorFamily.foreground(colors),
            BlendMode.srcIn,
          ),
        ),
      ),
    );
  }
}
