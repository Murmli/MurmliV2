import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../features/shopping_list/sync/shopping_list_data_type.dart';
import '../../features/shopping_list/sync/shopping_list_item_data_type.dart';
import 'sync_data_type.dart';

part 'sync_data_types_provider.g.dart';

@Riverpod(keepAlive: true)
List<SyncDataType> syncDataTypes(Ref ref) => const [
  ShoppingListDataType(),
  ShoppingListItemDataType(),
];
