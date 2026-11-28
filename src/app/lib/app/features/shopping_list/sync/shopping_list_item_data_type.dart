import 'package:drift/drift.dart';

import '../../../core/database/app_database.dart';
import '../../../core/sync/sync_command_kind.dart';
import '../../../core/sync/sync_data_type.dart';
import '../models/market_category.dart';
import '../models/measurement_unit.dart';
import 'sync_field_reading.dart';

const _maxNameLength = 256;
const _maxQuantity = 999999.999;
const _maxQuantityDecimals = 3;
final _languagePattern = RegExp(r'^[a-z]{2,3}$');

/// An item of a shopping list. Category and unit keep keys this version does
/// not know, so they survive until a newer version can show them.
class ShoppingListItemDataType implements SyncDataType {
  const ShoppingListItemDataType();

  @override
  String get name => 'shoppingListItem';

  @override
  String? get rootScopeKind => null;

  @override
  void checkLimits(SyncCommandKind kind, Map<String, Object?> fields) {
    final isCreate = kind == SyncCommandKind.create;
    void check(String field, {required bool isRequired, required bool valid}) {
      if ((isRequired || fields.containsKey(field)) && !valid) {
        throw SyncLimitExceededException(dataType: name, field: field);
      }
    }

    check(
      'name',
      isRequired: isCreate,
      valid: switch (fields['name']) {
        final String itemName =>
          itemName.isNotEmpty && itemName.length <= _maxNameLength,
        _ => false,
      },
    );
    final namesAmount =
        fields.containsKey('quantity') || fields.containsKey('unit');
    check(
      'quantity',
      isRequired: namesAmount,
      valid:
          fields.containsKey('quantity') &&
          switch (fields['quantity']) {
            null => true,
            final num quantity => _isValidQuantity(quantity),
            _ => false,
          },
    );
    check(
      'unit',
      isRequired: namesAmount,
      valid:
          fields.containsKey('unit') &&
          switch (fields['unit']) {
            null => true,
            final String unit => MeasurementUnit.fromKey(unit) != null,
            _ => false,
          },
    );
    check(
      'category',
      isRequired: isCreate,
      valid: switch (fields['category']) {
        final String category => MarketCategory.fromKey(category) != null,
        _ => false,
      },
    );
    check('checked', isRequired: false, valid: fields['checked'] is bool);
    check(
      'language',
      isRequired: isCreate,
      valid:
          isCreate &&
          switch (fields['language']) {
            final String language => _languagePattern.hasMatch(language),
            _ => false,
          },
    );
  }

  @override
  Future<void> writeServerRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) async {
    await database
        .into(database.shoppingListItems)
        .insertOnConflictUpdate(
          ShoppingListItemsCompanion.insert(
            id: recordId,
            scopeId: scopeId,
            name: readRequiredField<String>(fields, 'name'),
            quantity: Value(
              readOptionalField<num>(fields, 'quantity')?.toDouble(),
            ),
            unit: Value(readOptionalField<String>(fields, 'unit')),
            category: readRequiredField<String>(fields, 'category'),
            checked: Value(readOptionalField<bool>(fields, 'checked') ?? false),
            language: readRequiredField<String>(fields, 'language'),
            createdAt: Value(readOptionalField<int>(fields, 'createdAt')),
            changedAt: Value(readOptionalField<int>(fields, 'changedAt')),
            changedBy: Value(readOptionalField<String>(fields, 'changedBy')),
          ),
        );
  }

  @override
  Future<void> createRecordIfMissing(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
    required int time,
  }) => database
      .into(database.shoppingListItems)
      .insert(
        ShoppingListItemsCompanion.insert(
          id: recordId,
          scopeId: scopeId,
          name: fields['name'] as String? ?? '',
          quantity: Value((fields['quantity'] as num?)?.toDouble()),
          unit: Value(fields['unit'] as String?),
          category: fields['category'] as String? ?? '',
          checked: Value(fields['checked'] as bool? ?? false),
          language: fields['language'] as String? ?? '',
          createdAt: Value(time),
          changedAt: Value(time),
        ),
        mode: InsertMode.insertOrIgnore,
      );

  @override
  Future<void> updateRecordIfPresent(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
    required int time,
  }) async {
    final changes = ShoppingListItemsCompanion(
      name: _valueIfPresent(fields, 'name'),
      quantity: fields.containsKey('quantity')
          ? Value((fields['quantity'] as num?)?.toDouble())
          : const Value.absent(),
      unit: _valueIfPresent(fields, 'unit'),
      category: _valueIfPresent(fields, 'category'),
      checked: _valueIfPresent(fields, 'checked'),
    );
    if (changes.toColumns(false).isEmpty) {
      return;
    }
    await (database.update(database.shoppingListItems)..where(
          (item) => item.scopeId.equals(scopeId) & item.id.equals(recordId),
        ))
        .write(changes.copyWith(changedAt: Value(time)));
  }

  @override
  Future<void> deleteRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
  }) =>
      (database.delete(database.shoppingListItems)..where(
            (item) => item.scopeId.equals(scopeId) & item.id.equals(recordId),
          ))
          .go();

  @override
  Future<void> deleteScopeRecords(
    AppDatabase database,
    String scopeId, {
    Set<String> keptRecordIds = const {},
  }) =>
      (database.delete(database.shoppingListItems)..where(
            (item) =>
                item.scopeId.equals(scopeId) & item.id.isNotIn(keptRecordIds),
          ))
          .go();

  @override
  Future<void> deleteAllRecords(AppDatabase database) =>
      database.delete(database.shoppingListItems).go();

  @override
  Future<String?> scopeLabel(AppDatabase database, String scopeId) async =>
      null;
}

Value<T> _valueIfPresent<T>(Map<String, Object?> fields, String field) =>
    fields.containsKey(field)
    ? Value(fields[field] as T)
    : const Value.absent();

bool _isValidQuantity(num quantity) {
  if (quantity <= 0 || quantity > _maxQuantity) {
    return false;
  }
  // Within the allowed range only values with more than three decimals print
  // in exponent form, such as 1e-7.
  final text = quantity.toString();
  final decimalPoint = text.indexOf('.');
  return !text.contains('e') &&
      (decimalPoint == -1 ||
          text.length - decimalPoint - 1 <= _maxQuantityDecimals);
}
