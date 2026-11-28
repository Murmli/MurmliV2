import 'package:material_ui/material_ui.dart';

import '../../core/theme/app_colors.dart';
import 'models/market_category.dart';

enum MarketCategoryColorFamily {
  green,
  yellow,
  teal,
  red,
  brown,
  blue,
  cyan,
  indigo,
  pink,
  slate,
  lime,
  purple;

  Color foreground(AppColors colors) => switch (this) {
    MarketCategoryColorFamily.green => colors.categoryGreen,
    MarketCategoryColorFamily.yellow => colors.categoryYellow,
    MarketCategoryColorFamily.teal => colors.categoryTeal,
    MarketCategoryColorFamily.red => colors.categoryRed,
    MarketCategoryColorFamily.brown => colors.categoryBrown,
    MarketCategoryColorFamily.blue => colors.categoryBlue,
    MarketCategoryColorFamily.cyan => colors.categoryCyan,
    MarketCategoryColorFamily.indigo => colors.categoryIndigo,
    MarketCategoryColorFamily.pink => colors.categoryPink,
    MarketCategoryColorFamily.slate => colors.categorySlate,
    MarketCategoryColorFamily.lime => colors.categoryLime,
    MarketCategoryColorFamily.purple => colors.categoryPurple,
  };

  Color background(AppColors colors) => switch (this) {
    MarketCategoryColorFamily.green => colors.categoryGreenSoft,
    MarketCategoryColorFamily.yellow => colors.categoryYellowSoft,
    MarketCategoryColorFamily.teal => colors.categoryTealSoft,
    MarketCategoryColorFamily.red => colors.categoryRedSoft,
    MarketCategoryColorFamily.brown => colors.categoryBrownSoft,
    MarketCategoryColorFamily.blue => colors.categoryBlueSoft,
    MarketCategoryColorFamily.cyan => colors.categoryCyanSoft,
    MarketCategoryColorFamily.indigo => colors.categoryIndigoSoft,
    MarketCategoryColorFamily.pink => colors.categoryPinkSoft,
    MarketCategoryColorFamily.slate => colors.categorySlateSoft,
    MarketCategoryColorFamily.lime => colors.categoryLimeSoft,
    MarketCategoryColorFamily.purple => colors.categoryPurpleSoft,
  };
}

extension MarketCategoryAppearance on MarketCategory {
  String get iconAsset => 'assets/icons/categories/${_iconFileName()}.svg';

  String _iconFileName() => switch (this) {
    MarketCategory.vegetables => 'vegetables',
    MarketCategory.fruits => 'fruits',
    MarketCategory.bakery => 'bakery',
    MarketCategory.refrigerated => 'refrigerated',
    MarketCategory.dairy => 'dairy',
    MarketCategory.meatAndFish => 'meat_and_fish',
    MarketCategory.grainsAndPasta => 'grains_and_pasta',
    MarketCategory.cannedGoods => 'canned_goods',
    MarketCategory.brothsAndSauces => 'broths_and_sauces',
    MarketCategory.spices => 'spices',
    MarketCategory.vinegarAndOils => 'vinegar_and_oils',
    MarketCategory.bakingAndCooking => 'baking_and_cooking',
    MarketCategory.cerealsAndMuesli => 'cereals_and_muesli',
    MarketCategory.nuts => 'nuts',
    MarketCategory.snacks => 'snacks',
    MarketCategory.sweets => 'sweets',
    MarketCategory.beverages => 'beverages',
    MarketCategory.householdGoods => 'household_goods',
    MarketCategory.personalCare => 'personal_care',
    MarketCategory.petFood => 'pet_food',
    MarketCategory.frozen => 'frozen',
    MarketCategory.miscellaneous => 'miscellaneous',
  };

  MarketCategoryColorFamily get colorFamily => switch (this) {
    MarketCategory.vegetables => MarketCategoryColorFamily.green,
    MarketCategory.bakery ||
    MarketCategory.cerealsAndMuesli ||
    MarketCategory.vinegarAndOils => MarketCategoryColorFamily.yellow,
    MarketCategory.beverages => MarketCategoryColorFamily.teal,
    MarketCategory.fruits ||
    MarketCategory.brothsAndSauces => MarketCategoryColorFamily.red,
    MarketCategory.nuts ||
    MarketCategory.grainsAndPasta ||
    MarketCategory.petFood => MarketCategoryColorFamily.brown,
    MarketCategory.dairy ||
    MarketCategory.householdGoods => MarketCategoryColorFamily.blue,
    MarketCategory.frozen ||
    MarketCategory.refrigerated ||
    MarketCategory.personalCare => MarketCategoryColorFamily.cyan,
    MarketCategory.bakingAndCooking => MarketCategoryColorFamily.indigo,
    MarketCategory.meatAndFish ||
    MarketCategory.sweets => MarketCategoryColorFamily.pink,
    MarketCategory.cannedGoods ||
    MarketCategory.miscellaneous => MarketCategoryColorFamily.slate,
    MarketCategory.spices => MarketCategoryColorFamily.lime,
    MarketCategory.snacks => MarketCategoryColorFamily.purple,
  };
}
