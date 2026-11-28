import 'package:intl/intl.dart';

import '../../../i18n/strings.g.dart';
import 'models/shopping_list_item.dart';

String quantityLabel(
  ShoppingListItem item,
  Translations translations,
  String languageCode,
) => [
  if (item.quantity case final quantity?)
    formatQuantity(quantity, languageCode),
  ?item.unit?.abbreviation(translations),
].join(' ');

String formatQuantity(double quantity, String languageCode) =>
    NumberFormat('0.###', languageCode).format(quantity);
