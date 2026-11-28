import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/sync/sync_command_kind.dart';
import 'package:app/app/core/sync/sync_data_type.dart';
import 'package:app/app/features/shopping_list/sync/shopping_list_item_data_type.dart';

const _validCreate = {'name': 'Milk', 'category': 'dairy', 'language': 'de'};

const _serverFields = {
  'name': 'Milk',
  'quantity': 2,
  'unit': 'liter',
  'category': 'dairy',
  'checked': true,
  'language': 'de',
  'createdAt': 1000,
  'changedAt': 2000,
  'changedBy': 'member',
};

Map<String, Object?> _without(Map<String, Object?> fields, String field) =>
    {...fields}..remove(field);

void main() {
  const dataType = ShoppingListItemDataType();
  late AppDatabase database;

  setUp(() {
    database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);
  });

  Future<List<ShoppingListItemRow>> readItems() =>
      database.select(database.shoppingListItems).get();

  Future<void> writeServerItem(Map<String, Object?> fields) =>
      dataType.writeServerRecord(
        database,
        scopeId: 'list',
        recordId: 'item',
        fields: fields,
      );

  Matcher throwsLimitFor(String field) => throwsA(
    isA<SyncLimitExceededException>().having(
      (exception) => exception.field,
      'field',
      field,
    ),
  );

  group('checkLimits', () {
    test('a create with every field at its limits passes', () {
      dataType.checkLimits(SyncCommandKind.create, {
        ..._validCreate,
        'name': 'a' * 256,
        'quantity': 999999.999,
        'unit': 'gram',
        'checked': true,
        'language': 'deu',
      });
    });

    for (final (description, fields, field) in [
      ('without a name', _without(_validCreate, 'name'), 'name'),
      ('with an empty name', {..._validCreate, 'name': ''}, 'name'),
      (
        'with a name of 257 characters',
        {..._validCreate, 'name': 'a' * 257},
        'name',
      ),
      ('without a category', _without(_validCreate, 'category'), 'category'),
      (
        'with an unknown category',
        {..._validCreate, 'category': 'toys'},
        'category',
      ),
      ('without a language', _without(_validCreate, 'language'), 'language'),
      (
        'with an uppercase language',
        {..._validCreate, 'language': 'DE'},
        'language',
      ),
      (
        'with a language followed by a line break',
        {..._validCreate, 'language': 'de\n'},
        'language',
      ),
      (
        'with a checked value that is no boolean',
        {..._validCreate, 'checked': 'yes'},
        'checked',
      ),
    ]) {
      test('a create $description throws for $field', () {
        expect(
          () => dataType.checkLimits(SyncCommandKind.create, fields),
          throwsLimitFor(field),
        );
      });
    }

    for (final (description, fields, field) in [
      ('a quantity without a unit', {'quantity': 1}, 'unit'),
      ('a unit without a quantity', {'unit': 'gram'}, 'quantity'),
      ('a quantity of zero', {'quantity': 0, 'unit': null}, 'quantity'),
      (
        'a quantity above the maximum',
        {'quantity': 1000000, 'unit': null},
        'quantity',
      ),
      (
        'a quantity with four decimals',
        {'quantity': 1.0005, 'unit': null},
        'quantity',
      ),
      ('a tiny quantity', {'quantity': 0.0000001, 'unit': null}, 'quantity'),
      ('an unknown unit', {'quantity': null, 'unit': 'bucket'}, 'unit'),
      ('a language', {'language': 'de'}, 'language'),
      ('a null name', {'name': null}, 'name'),
    ]) {
      test('a setFields with $description throws for $field', () {
        expect(
          () => dataType.checkLimits(SyncCommandKind.setFields, fields),
          throwsLimitFor(field),
        );
      });
    }

    test('a setFields clearing the amount passes', () {
      dataType.checkLimits(SyncCommandKind.setFields, {
        'quantity': null,
        'unit': null,
      });
    });

    test('a setFields with a quantity of three decimals passes', () {
      dataType.checkLimits(SyncCommandKind.setFields, {
        'quantity': 0.125,
        'unit': 'kilogram',
      });
    });
  });

  group('writeServerRecord', () {
    test('a complete record is stored with its server fields', () async {
      await writeServerItem(_serverFields);

      expect(await readItems(), [
        const ShoppingListItemRow(
          id: 'item',
          scopeId: 'list',
          name: 'Milk',
          quantity: 2,
          unit: 'liter',
          category: 'dairy',
          checked: true,
          language: 'de',
          createdAt: 1000,
          changedAt: 2000,
          changedBy: 'member',
        ),
      ]);
    });

    test(
      'a replacement keeps unknown keys and ignores unknown fields',
      () async {
        await writeServerItem(_serverFields);

        await writeServerItem({
          ..._serverFields,
          'unit': 'bucket',
          'category': 'toys',
          'note': 'unknown',
        });

        final item = (await readItems()).single;
        expect((item.unit, item.category), ('bucket', 'toys'));
      },
    );

    for (final (description, fields) in [
      ('a missing name', _without(_serverFields, 'name')),
      ('a missing category', _without(_serverFields, 'category')),
      ('a missing language', _without(_serverFields, 'language')),
      ('a name that is no text', {..._serverFields, 'name': 1}),
      ('a quantity that is no number', {..._serverFields, 'quantity': '2'}),
    ]) {
      test('a record with $description throws a FormatException', () {
        expect(writeServerItem(fields), throwsFormatException);
      });
    }
  });

  group('updateRecordIfPresent and deleteRecord', () {
    test('an item id in another scope stays unchanged', () async {
      await writeServerItem(_serverFields);

      await dataType.updateRecordIfPresent(
        database,
        scopeId: 'other',
        recordId: 'item',
        fields: {'checked': false},
        time: 3000,
      );
      await dataType.deleteRecord(database, scopeId: 'other', recordId: 'item');

      expect((await readItems()).single.checked, isTrue);
    });
  });

  group('createRecordIfMissing', () {
    test('a new item is stored with the command time as created and changed '
        'time', () async {
      await dataType.createRecordIfMissing(
        database,
        scopeId: 'list',
        recordId: 'item',
        fields: {
          ..._validCreate,
          'quantity': 2,
          'unit': 'liter',
          'checked': false,
        },
        time: 3000,
      );

      expect(await readItems(), [
        const ShoppingListItemRow(
          id: 'item',
          scopeId: 'list',
          name: 'Milk',
          quantity: 2,
          unit: 'liter',
          category: 'dairy',
          checked: false,
          language: 'de',
          createdAt: 3000,
          changedAt: 3000,
        ),
      ]);
    });
  });

  group('updateRecordIfPresent', () {
    test('changes only the named fields and takes the command time as '
        'changed time', () async {
      await writeServerItem(_serverFields);

      await dataType.updateRecordIfPresent(
        database,
        scopeId: 'list',
        recordId: 'item',
        fields: {'quantity': null, 'unit': null},
        time: 3000,
      );

      final item = (await readItems()).single;
      expect(
        (item.name, item.quantity, item.unit, item.checked, item.changedAt),
        ('Milk', null, null, true, 3000),
      );
    });
  });

  group('deleteScopeRecords', () {
    test('keeps the kept items and the items of other scopes', () async {
      for (final (scopeId, recordId) in [
        ('list', 'kept'),
        ('list', 'removed'),
        ('other', 'other-item'),
      ]) {
        await dataType.writeServerRecord(
          database,
          scopeId: scopeId,
          recordId: recordId,
          fields: _serverFields,
        );
      }

      await dataType.deleteScopeRecords(
        database,
        'list',
        keptRecordIds: {'kept'},
      );

      expect(
        [for (final item in await readItems()) item.id],
        ['kept', 'other-item'],
      );
    });
  });
}
