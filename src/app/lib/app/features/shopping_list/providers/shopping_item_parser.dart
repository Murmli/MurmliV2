import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../models/parsed_shopping_item.dart';
import 'mock_shopping_item_parser.dart';

part 'shopping_item_parser.g.dart';

@Riverpod(keepAlive: true)
ShoppingItemParser shoppingItemParser(Ref ref) =>
    const MockShoppingItemParser(delay: Duration(milliseconds: 900));

abstract interface class ShoppingItemParser {
  Future<List<ParsedShoppingItem>> parse(String text);
}
