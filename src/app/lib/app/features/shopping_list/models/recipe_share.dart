import 'package:dart_mappable/dart_mappable.dart';

part 'recipe_share.mapper.dart';

@MappableClass()
class RecipeShare with RecipeShareMappable {
  const RecipeShare({
    required this.recipeId,
    required this.recipeTitle,
    required this.quantity,
  });

  final String recipeId;
  final String recipeTitle;
  final double? quantity;
}
