import '../../../../i18n/strings.g.dart';

enum MarketCategory {
  vegetables('vegetables'),
  fruits('fruits'),
  bakery('bakery'),
  refrigerated('refrigerated'),
  dairy('dairy'),
  meatAndFish('meatAndFish'),
  grainsAndPasta('grainsAndPasta'),
  cannedGoods('cannedGoods'),
  brothsAndSauces('brothsAndSauces'),
  spices('spices'),
  vinegarAndOils('vinegarAndOils'),
  bakingAndCooking('bakingAndCooking'),
  cerealsAndMuesli('cerealsAndMuesli'),
  nuts('nuts'),
  snacks('snacks'),
  sweets('sweets'),
  beverages('beverages'),
  householdGoods('householdGoods'),
  personalCare('personalCare'),
  petFood('petFood'),
  frozen('frozen'),
  miscellaneous('miscellaneous');

  const MarketCategory(this.key);

  final String key;

  static final _byKey = {for (final category in values) category.key: category};

  static MarketCategory? fromKey(String key) => _byKey[key];

  String label(Translations translations) => switch (this) {
    MarketCategory.vegetables =>
      translations.shoppingList.categories.vegetables,
    MarketCategory.fruits => translations.shoppingList.categories.fruits,
    MarketCategory.bakery => translations.shoppingList.categories.bakery,
    MarketCategory.refrigerated =>
      translations.shoppingList.categories.refrigerated,
    MarketCategory.dairy => translations.shoppingList.categories.dairy,
    MarketCategory.meatAndFish =>
      translations.shoppingList.categories.meatAndFish,
    MarketCategory.grainsAndPasta =>
      translations.shoppingList.categories.grainsAndPasta,
    MarketCategory.cannedGoods =>
      translations.shoppingList.categories.cannedGoods,
    MarketCategory.brothsAndSauces =>
      translations.shoppingList.categories.brothsAndSauces,
    MarketCategory.spices => translations.shoppingList.categories.spices,
    MarketCategory.vinegarAndOils =>
      translations.shoppingList.categories.vinegarAndOils,
    MarketCategory.bakingAndCooking =>
      translations.shoppingList.categories.bakingAndCooking,
    MarketCategory.cerealsAndMuesli =>
      translations.shoppingList.categories.cerealsAndMuesli,
    MarketCategory.nuts => translations.shoppingList.categories.nuts,
    MarketCategory.snacks => translations.shoppingList.categories.snacks,
    MarketCategory.sweets => translations.shoppingList.categories.sweets,
    MarketCategory.beverages => translations.shoppingList.categories.beverages,
    MarketCategory.householdGoods =>
      translations.shoppingList.categories.householdGoods,
    MarketCategory.personalCare =>
      translations.shoppingList.categories.personalCare,
    MarketCategory.petFood => translations.shoppingList.categories.petFood,
    MarketCategory.frozen => translations.shoppingList.categories.frozen,
    MarketCategory.miscellaneous =>
      translations.shoppingList.categories.miscellaneous,
  };
}
