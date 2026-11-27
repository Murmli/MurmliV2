import 'package:drift/drift.dart';

import '../../../i18n/strings.g.dart';
import '../../core/database/app_database.dart';
import '../../core/identifiers/new_uuid.dart';
import 'models/market_category.dart';
import 'models/measurement_unit.dart';

typedef _SampleRecipe = ({String id, String germanTitle, String englishTitle});

typedef _SampleItem = ({
  String germanName,
  String englishName,
  double? quantity,
  MeasurementUnit? unit,
  MarketCategory category,
  bool checked,
  List<(_SampleRecipe, double)> recipeShares,
});

const _goulash = (
  id: '6dfe8404-a585-471d-a774-cbf5f1abdaac',
  germanTitle: 'Gulasch',
  englishTitle: 'Goulash',
);
const _bakedPotatoes = (
  id: '528b0b59-fd6b-4fc8-9123-c2d8d6d32ece',
  germanTitle: 'Ofenkartoffeln',
  englishTitle: 'Baked potatoes',
);

const List<_SampleItem> _sampleItems = [
  (
    germanName: 'Paprika',
    englishName: 'Bell peppers',
    quantity: 3,
    unit: MeasurementUnit.piece,
    category: MarketCategory.vegetables,
    checked: false,
    recipeShares: [(_goulash, 3)],
  ),
  (
    germanName: 'Zwiebeln',
    englishName: 'Onions',
    quantity: 1,
    unit: MeasurementUnit.kilogram,
    category: MarketCategory.vegetables,
    checked: false,
    recipeShares: [(_goulash, 1)],
  ),
  (
    germanName: 'Kartoffeln',
    englishName: 'Potatoes',
    quantity: 2,
    unit: MeasurementUnit.kilogram,
    category: MarketCategory.vegetables,
    checked: false,
    recipeShares: [(_bakedPotatoes, 2)],
  ),
  (
    germanName: 'Äpfel',
    englishName: 'Apples',
    quantity: 6,
    unit: MeasurementUnit.piece,
    category: MarketCategory.fruits,
    checked: false,
    recipeShares: [],
  ),
  (
    germanName: 'Bananen',
    englishName: 'Bananas',
    quantity: 5,
    unit: MeasurementUnit.piece,
    category: MarketCategory.fruits,
    checked: true,
    recipeShares: [],
  ),
  (
    germanName: 'Vollkornbrot',
    englishName: 'Wholegrain bread',
    quantity: 1,
    unit: MeasurementUnit.piece,
    category: MarketCategory.bakery,
    checked: false,
    recipeShares: [],
  ),
  (
    germanName: 'Eier',
    englishName: 'Eggs',
    quantity: 6,
    unit: MeasurementUnit.piece,
    category: MarketCategory.refrigerated,
    checked: false,
    recipeShares: [(_goulash, 2)],
  ),
  (
    germanName: 'Sauerrahm',
    englishName: 'Sour cream',
    quantity: 500,
    unit: MeasurementUnit.gram,
    category: MarketCategory.dairy,
    checked: false,
    recipeShares: [(_goulash, 200), (_bakedPotatoes, 300)],
  ),
  (
    germanName: 'Milch',
    englishName: 'Milk',
    quantity: 1,
    unit: MeasurementUnit.liter,
    category: MarketCategory.dairy,
    checked: true,
    recipeShares: [],
  ),
  (
    germanName: 'Rindergulasch',
    englishName: 'Stewing beef',
    quantity: 800,
    unit: MeasurementUnit.gram,
    category: MarketCategory.meatAndFish,
    checked: false,
    recipeShares: [(_goulash, 800)],
  ),
  (
    germanName: 'Spaghetti',
    englishName: 'Spaghetti',
    quantity: 500,
    unit: MeasurementUnit.gram,
    category: MarketCategory.grainsAndPasta,
    checked: false,
    recipeShares: [],
  ),
  (
    germanName: 'Gehackte Tomaten',
    englishName: 'Chopped tomatoes',
    quantity: 2,
    unit: MeasurementUnit.can,
    category: MarketCategory.cannedGoods,
    checked: false,
    recipeShares: [],
  ),
  (
    germanName: 'Paprikapulver',
    englishName: 'Paprika powder',
    quantity: 2,
    unit: MeasurementUnit.tablespoon,
    category: MarketCategory.spices,
    checked: false,
    recipeShares: [(_goulash, 2)],
  ),
  (
    germanName: 'Olivenöl',
    englishName: 'Olive oil',
    quantity: null,
    unit: null,
    category: MarketCategory.vinegarAndOils,
    checked: false,
    recipeShares: [],
  ),
  (
    germanName: 'Mineralwasser',
    englishName: 'Mineral water',
    quantity: 6,
    unit: MeasurementUnit.liter,
    category: MarketCategory.beverages,
    checked: false,
    recipeShares: [],
  ),
  (
    germanName: 'Spülmittel',
    englishName: 'Dish soap',
    quantity: null,
    unit: null,
    category: MarketCategory.householdGoods,
    checked: false,
    recipeShares: [],
  ),
];

Future<void> insertSampleShoppingListItems(
  AppDatabase database, {
  required String listId,
  required DateTime now,
}) {
  final language = LocaleSettings.currentLocale.languageCode;
  final isGerman = language == AppLocale.de.languageCode;
  return database.batch((batch) {
    for (final (index, sampleItem) in _sampleItems.indexed) {
      final itemId = newUuid();
      // Distinct creation times keep the sample order stable in the list.
      final createdAt = now.millisecondsSinceEpoch + index;
      batch.insert(
        database.shoppingListItems,
        ShoppingListItemsCompanion.insert(
          id: itemId,
          scopeId: listId,
          name: isGerman ? sampleItem.germanName : sampleItem.englishName,
          quantity: Value(sampleItem.quantity),
          unit: Value(sampleItem.unit?.key),
          category: sampleItem.category.key,
          checked: Value(sampleItem.checked),
          language: language,
          createdAt: Value(createdAt),
          changedAt: Value(createdAt),
        ),
      );
      for (final (recipe, quantity) in sampleItem.recipeShares) {
        batch.insert(
          database.shoppingListItemRecipes,
          ShoppingListItemRecipesCompanion.insert(
            itemId: itemId,
            recipeId: recipe.id,
            recipeTitle: isGerman ? recipe.germanTitle : recipe.englishTitle,
            quantity: Value(quantity),
          ),
        );
      }
    }
  });
}
