import '../../../i18n/strings.g.dart';
import 'models/market_category.dart';
import 'models/measurement_unit.dart';
import 'providers/shopping_list_repository.dart';

typedef _SampleItem = ({
  String germanName,
  String englishName,
  double? quantity,
  MeasurementUnit? unit,
  MarketCategory category,
  bool checked,
});

const List<_SampleItem> _sampleItems = [
  (
    germanName: 'Paprika',
    englishName: 'Bell peppers',
    quantity: 3,
    unit: MeasurementUnit.piece,
    category: MarketCategory.vegetables,
    checked: false,
  ),
  (
    germanName: 'Zwiebeln',
    englishName: 'Onions',
    quantity: 1,
    unit: MeasurementUnit.kilogram,
    category: MarketCategory.vegetables,
    checked: false,
  ),
  (
    germanName: 'Kartoffeln',
    englishName: 'Potatoes',
    quantity: 2,
    unit: MeasurementUnit.kilogram,
    category: MarketCategory.vegetables,
    checked: false,
  ),
  (
    germanName: 'Äpfel',
    englishName: 'Apples',
    quantity: 6,
    unit: MeasurementUnit.piece,
    category: MarketCategory.fruits,
    checked: false,
  ),
  (
    germanName: 'Bananen',
    englishName: 'Bananas',
    quantity: 5,
    unit: MeasurementUnit.piece,
    category: MarketCategory.fruits,
    checked: true,
  ),
  (
    germanName: 'Vollkornbrot',
    englishName: 'Wholegrain bread',
    quantity: 1,
    unit: MeasurementUnit.piece,
    category: MarketCategory.bakery,
    checked: false,
  ),
  (
    germanName: 'Eier',
    englishName: 'Eggs',
    quantity: 6,
    unit: MeasurementUnit.piece,
    category: MarketCategory.refrigerated,
    checked: false,
  ),
  (
    germanName: 'Sauerrahm',
    englishName: 'Sour cream',
    quantity: 500,
    unit: MeasurementUnit.gram,
    category: MarketCategory.dairy,
    checked: false,
  ),
  (
    germanName: 'Milch',
    englishName: 'Milk',
    quantity: 1,
    unit: MeasurementUnit.liter,
    category: MarketCategory.dairy,
    checked: true,
  ),
  (
    germanName: 'Rindergulasch',
    englishName: 'Stewing beef',
    quantity: 800,
    unit: MeasurementUnit.gram,
    category: MarketCategory.meatAndFish,
    checked: false,
  ),
  (
    germanName: 'Spaghetti',
    englishName: 'Spaghetti',
    quantity: 500,
    unit: MeasurementUnit.gram,
    category: MarketCategory.grainsAndPasta,
    checked: false,
  ),
  (
    germanName: 'Gehackte Tomaten',
    englishName: 'Chopped tomatoes',
    quantity: 2,
    unit: MeasurementUnit.can,
    category: MarketCategory.cannedGoods,
    checked: false,
  ),
  (
    germanName: 'Paprikapulver',
    englishName: 'Paprika powder',
    quantity: 2,
    unit: MeasurementUnit.tablespoon,
    category: MarketCategory.spices,
    checked: false,
  ),
  (
    germanName: 'Olivenöl',
    englishName: 'Olive oil',
    quantity: null,
    unit: null,
    category: MarketCategory.vinegarAndOils,
    checked: false,
  ),
  (
    germanName: 'Mineralwasser',
    englishName: 'Mineral water',
    quantity: 6,
    unit: MeasurementUnit.liter,
    category: MarketCategory.beverages,
    checked: false,
  ),
  (
    germanName: 'Spülmittel',
    englishName: 'Dish soap',
    quantity: null,
    unit: null,
    category: MarketCategory.householdGoods,
    checked: false,
  ),
];

/// Adds the sample items to the list in the language of the app, one write
/// per item, so they reach the server like items typed by hand.
Future<void> addSampleShoppingListItems(
  ShoppingListRepository repository, {
  required String listId,
}) async {
  final isGerman =
      LocaleSettings.currentLocale.languageCode == AppLocale.de.languageCode;
  for (final sampleItem in _sampleItems) {
    final itemId = await repository.addItem(
      listId: listId,
      name: isGerman ? sampleItem.germanName : sampleItem.englishName,
      category: sampleItem.category,
      quantity: sampleItem.quantity,
      unit: sampleItem.unit,
    );
    if (sampleItem.checked) {
      await repository.setChecked(itemId, checked: true);
    }
  }
}
