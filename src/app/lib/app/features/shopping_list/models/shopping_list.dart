import 'package:dart_mappable/dart_mappable.dart';

import 'shopping_list_item.dart';

part 'shopping_list.mapper.dart';

@MappableClass()
class ShoppingList with ShoppingListMappable {
  const ShoppingList({
    required this.id,
    required this.name,
    required this.items,
  });

  final String id;
  final String name;
  final List<ShoppingListItem> items;
}
