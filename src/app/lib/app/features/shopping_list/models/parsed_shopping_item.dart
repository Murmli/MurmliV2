import 'package:dart_mappable/dart_mappable.dart';

import 'market_category.dart';
import 'measurement_unit.dart';

part 'parsed_shopping_item.mapper.dart';

@MappableClass()
class ParsedShoppingItem with ParsedShoppingItemMappable {
  const ParsedShoppingItem({
    required this.name,
    required this.category,
    this.quantity,
    this.unit,
  });

  final String name;
  final double? quantity;
  final MeasurementUnit? unit;
  final MarketCategory category;
}
