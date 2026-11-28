import 'package:intl/intl.dart';

import '../../../i18n/strings.g.dart';
import 'models/shopping_list_item.dart';

String quantityLabel(
  ShoppingListItem item,
  Translations translations,
  String languageCode,
) => [
  if (item.quantity case final quantity?)
    NumberFormat('0.###', languageCode).format(quantity),
  ?item.unit?.abbreviation(translations),
].join(' ');
