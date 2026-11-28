import 'package:dart_mappable/dart_mappable.dart';

import 'shopping_list_group.dart';
import 'shopping_list_item.dart';

part 'shopping_list_overview.mapper.dart';

@MappableClass()
class ShoppingListOverview with ShoppingListOverviewMappable {
  const ShoppingListOverview({
    required this.listId,
    required this.listName,
    required this.checkedCount,
    required this.totalCount,
    required this.groups,
    required this.checkedItems,
    required this.leavingCheckedItemIds,
  });

  final String listId;
  final String listName;
  final int checkedCount;
  final int totalCount;
  final List<ShoppingListGroup> groups;
  final List<ShoppingListItem> checkedItems;
  final Set<String> leavingCheckedItemIds;
}
