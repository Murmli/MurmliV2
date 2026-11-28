import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/sync/sync_command_kind.dart';
import 'package:app/app/core/sync/sync_data_type.dart';
import 'package:app/app/features/shopping_list/sync/shopping_list_data_type.dart';

void main() {
  const dataType = ShoppingListDataType();
  late AppDatabase database;

  setUp(() {
    database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);
  });

  Future<List<ShoppingListRow>> readLists() =>
      database.select(database.shoppingLists).get();

  Future<void> writeServerList(Map<String, Object?> fields) =>
      dataType.writeServerRecord(
        database,
        scopeId: 'list',
        recordId: 'list',
        fields: fields,
      );

  group('checkLimits', () {
    test('a create with a name of 100 characters passes', () {
      dataType.checkLimits(SyncCommandKind.create, {'name': 'a' * 100});
    });

    test('a create with an empty name passes', () {
      dataType.checkLimits(SyncCommandKind.create, {'name': ''});
    });

    for (final (description, fields) in [
      ('without a name', <String, Object?>{}),
      ('with a name of 101 characters', {'name': 'a' * 101}),
      ('with a null name', {'name': null}),
    ]) {
      test('a create $description throws', () {
        expect(
          () => dataType.checkLimits(SyncCommandKind.create, fields),
          throwsA(isA<SyncLimitExceededException>()),
        );
      });
    }

    test('a setFields without a name passes', () {
      dataType.checkLimits(SyncCommandKind.setFields, {});
    });
  });

  group('writeServerRecord', () {
    test('a known list is replaced and unknown fields are ignored', () async {
      await writeServerList({'name': 'Old'});

      await writeServerList({'name': 'New', 'note': 'unknown'});

      expect(await readLists(), [
        const ShoppingListRow(id: 'list', name: 'New'),
      ]);
    });

    test('a record whose id differs from its scope throws a '
        'FormatException', () {
      expect(
        dataType.writeServerRecord(
          database,
          scopeId: 'list',
          recordId: 'other',
          fields: {'name': 'List'},
        ),
        throwsFormatException,
      );
    });

    for (final (description, fields) in [
      ('a missing name', <String, Object?>{}),
      ('a name that is no text', {'name': 1}),
    ]) {
      test('a record with $description throws a FormatException', () {
        expect(writeServerList(fields), throwsFormatException);
      });
    }
  });

  group('updateRecordIfPresent', () {
    test('a record id of another scope changes nothing', () async {
      await writeServerList({'name': 'List'});

      await dataType.updateRecordIfPresent(
        database,
        scopeId: 'other',
        recordId: 'list',
        fields: {'name': 'Renamed'},
        time: 1000,
      );

      expect(await readLists(), [
        const ShoppingListRow(id: 'list', name: 'List'),
      ]);
    });
  });

  group('scopeLabel', () {
    test('a list with a name is labeled with it', () async {
      await writeServerList({'name': 'Weekend'});

      expect(await dataType.scopeLabel(database, 'list'), 'Weekend');
    });

    test('a list with an empty name has no label', () async {
      await writeServerList({'name': ''});

      expect(await dataType.scopeLabel(database, 'list'), isNull);
    });
  });
}
