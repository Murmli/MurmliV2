import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/features/shopping_list/models/market_category.dart';
import 'package:app/app/features/shopping_list/models/measurement_unit.dart';
import 'package:app/app/features/shopping_list/models/parsed_shopping_item.dart';
import 'package:app/app/features/shopping_list/providers/mock_shopping_item_parser.dart';

void main() {
  const parser = MockShoppingItemParser(delay: Duration.zero);

  const milk = [
    ParsedShoppingItem(
      name: 'Milch',
      quantity: 2,
      unit: MeasurementUnit.liter,
      category: MarketCategory.dairy,
    ),
  ];
  const milkBag = [
    ParsedShoppingItem(
      name: 'Milch',
      quantity: 1,
      unit: MeasurementUnit.liter,
      category: MarketCategory.dairy,
    ),
  ];
  const mincedMeat = [
    ParsedShoppingItem(
      name: 'Hackfleisch',
      quantity: 500,
      unit: MeasurementUnit.gram,
      category: MarketCategory.meatAndFish,
    ),
  ];
  const bananasBreadAndTomatoes = [
    ParsedShoppingItem(name: 'Bananen', category: MarketCategory.fruits),
    ParsedShoppingItem(name: 'Brot', category: MarketCategory.bakery),
    ParsedShoppingItem(name: 'Tomaten', category: MarketCategory.vegetables),
  ];

  group('MockShoppingItemParser.parse', () {
    for (final (text, expected) in [
      ('2 Liter Milch', milk),
      ('  2 LITER Milch ', milk),
      ('ein Beutel Mich', milkBag),
      (' EIN BEUTEL MICH  ', milkBag),
      ('500g Hackfleisch', mincedMeat),
      ('\t500G hackfleisch ', mincedMeat),
      ('Bananen, Brot und Tomaten', bananasBreadAndTomatoes),
      ('  bananen, BROT und tomaten ', bananasBreadAndTomatoes),
    ]) {
      test('known input "$text" returns its items', () async {
        expect(await parser.parse(text), expected);
      });
    }

    for (final text in ['Spülmaschinentabs', 'Käse und Wurst']) {
      test(
        'unknown input "$text" returns one miscellaneous item named after the text',
        () async {
          expect(await parser.parse(' $text '), [
            ParsedShoppingItem(
              name: text,
              category: MarketCategory.miscellaneous,
            ),
          ]);
        },
      );
    }
  });
}
