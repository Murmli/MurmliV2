import 'package:dart_mappable/dart_mappable.dart';

import 'shopping_list_group.dart';

part 'shopping_list_overview.mapper.dart';

@MappableClass()
class ShoppingListOverview with ShoppingListOverviewMappable {
  const ShoppingListOverview({
    required this.checkedCount,
    required this.totalCount,
    required this.groups,
  });

  final int checkedCount;
  final int totalCount;
  final List<ShoppingListGroup> groups;
}
