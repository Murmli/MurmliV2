import '../../../i18n/strings.g.dart';

String shoppingListDisplayName(String name, Translations translations) =>
    name.isEmpty ? translations.shoppingList.defaultName : name;
