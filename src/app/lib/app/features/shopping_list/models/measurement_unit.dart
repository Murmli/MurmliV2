import '../../../../i18n/strings.g.dart';

enum MeasurementUnit {
  gram('gram'),
  kilogram('kilogram'),
  milliliter('milliliter'),
  liter('liter'),
  piece('piece'),
  tablespoon('tablespoon'),
  teaspoon('teaspoon'),
  pinch('pinch'),
  slice('slice'),
  can('can'),
  package('package');

  const MeasurementUnit(this.key);

  final String key;

  static final _byKey = {for (final unit in values) unit.key: unit};

  static MeasurementUnit? fromKey(String key) => _byKey[key];

  String label(Translations translations) => switch (this) {
    MeasurementUnit.gram => translations.shoppingList.units.gram.name,
    MeasurementUnit.kilogram => translations.shoppingList.units.kilogram.name,
    MeasurementUnit.milliliter =>
      translations.shoppingList.units.milliliter.name,
    MeasurementUnit.liter => translations.shoppingList.units.liter.name,
    MeasurementUnit.piece => translations.shoppingList.units.piece.name,
    MeasurementUnit.tablespoon =>
      translations.shoppingList.units.tablespoon.name,
    MeasurementUnit.teaspoon => translations.shoppingList.units.teaspoon.name,
    MeasurementUnit.pinch => translations.shoppingList.units.pinch.name,
    MeasurementUnit.slice => translations.shoppingList.units.slice.name,
    MeasurementUnit.can => translations.shoppingList.units.can.name,
    MeasurementUnit.package => translations.shoppingList.units.package.name,
  };

  String abbreviation(Translations translations) => switch (this) {
    MeasurementUnit.gram => translations.shoppingList.units.gram.abbreviation,
    MeasurementUnit.kilogram =>
      translations.shoppingList.units.kilogram.abbreviation,
    MeasurementUnit.milliliter =>
      translations.shoppingList.units.milliliter.abbreviation,
    MeasurementUnit.liter => translations.shoppingList.units.liter.abbreviation,
    MeasurementUnit.piece => translations.shoppingList.units.piece.abbreviation,
    MeasurementUnit.tablespoon =>
      translations.shoppingList.units.tablespoon.abbreviation,
    MeasurementUnit.teaspoon =>
      translations.shoppingList.units.teaspoon.abbreviation,
    MeasurementUnit.pinch => translations.shoppingList.units.pinch.abbreviation,
    MeasurementUnit.slice => translations.shoppingList.units.slice.abbreviation,
    MeasurementUnit.can => translations.shoppingList.units.can.abbreviation,
    MeasurementUnit.package =>
      translations.shoppingList.units.package.abbreviation,
  };
}
