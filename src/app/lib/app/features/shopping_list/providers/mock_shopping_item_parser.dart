import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import '../models/parsed_shopping_item.dart';
import 'shopping_item_parser.dart';

class MockShoppingItemParser implements ShoppingItemParser {
  const MockShoppingItemParser({required this.delay});

  final Duration delay;

  @override
  Future<List<ParsedShoppingItem>> parse(String text) async {
    await Future<void>.delayed(delay);
    return switch (text.trim().toLowerCase()) {
      '2 liter milch' => const [
        ParsedShoppingItem(
          name: 'Milch',
          quantity: 2,
          unit: MeasurementUnit.liter,
          category: MarketCategory.dairy,
        ),
      ],
      'ein beutel mich' => const [
        ParsedShoppingItem(
          name: 'Milch',
          quantity: 1,
          unit: MeasurementUnit.liter,
          category: MarketCategory.dairy,
        ),
      ],
      '500g hackfleisch' => const [
        ParsedShoppingItem(
          name: 'Hackfleisch',
          quantity: 500,
          unit: MeasurementUnit.gram,
          category: MarketCategory.meatAndFish,
        ),
      ],
      'bananen, brot und tomaten' => const [
        ParsedShoppingItem(name: 'Bananen', category: MarketCategory.fruits),
        ParsedShoppingItem(name: 'Brot', category: MarketCategory.bakery),
        ParsedShoppingItem(
          name: 'Tomaten',
          category: MarketCategory.vegetables,
        ),
      ],
      _ => [
        ParsedShoppingItem(
          name: text.trim(),
          category: MarketCategory.miscellaneous,
        ),
      ],
    };
  }
}
