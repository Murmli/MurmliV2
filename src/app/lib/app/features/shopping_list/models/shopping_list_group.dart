import 'package:dart_mappable/dart_mappable.dart';

import 'market_category.dart';
import 'shopping_list_item.dart';

part 'shopping_list_group.mapper.dart';

@MappableClass()
class ShoppingListGroup with ShoppingListGroupMappable {
  const ShoppingListGroup({
    required this.category,
    required this.items,
    required this.leavingItemIds,
  });

  final MarketCategory category;
  final List<ShoppingListItem> items;
  final Set<String> leavingItemIds;

  int get count => items.length - leavingItemIds.length;
}
