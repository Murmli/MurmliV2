import 'dart:math' as math;

import 'package:dart_mappable/dart_mappable.dart';

import 'market_category.dart';
import 'measurement_unit.dart';
import 'recipe_share.dart';

part 'shopping_list_item.mapper.dart';

@MappableClass()
class ShoppingListItem with ShoppingListItemMappable {
  const ShoppingListItem({
    required this.id,
    required this.listId,
    required this.name,
    required this.quantity,
    required this.unit,
    required this.category,
    required this.checked,
    required this.createdAt,
    required this.changedAt,
    required this.recipeShares,
  });

  final String id;
  final String listId;
  final String name;
  final double? quantity;
  final MeasurementUnit? unit;
  final MarketCategory category;
  final bool checked;
  final DateTime? createdAt;
  final DateTime? changedAt;
  final List<RecipeShare> recipeShares;

  double? get ownQuantity {
    final totalQuantity = quantity;
    if (totalQuantity == null) {
      return null;
    }
    final recipeQuantity = recipeShares.fold<double>(
      0,
      (sum, share) => sum + (share.quantity ?? 0),
    );
    return math.max(0, totalQuantity - recipeQuantity);
  }
}
