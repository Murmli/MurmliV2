import 'dart:convert';

import 'package:drift/drift.dart';

import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/sync/sync_command_kind.dart';
import 'package:app/app/core/sync/sync_data_type.dart';

const testNoteMaxTextLength = 20;

const testNote = TestDataType(
  name: 'testNote',
  table: 'test_notes',
  field: 'text',
  maxFieldLength: testNoteMaxTextLength,
);

/// The root record of a shared scope, its id is the id of the scope.
const testBoard = TestDataType(
  name: 'testBoard',
  table: 'test_boards',
  field: 'title',
  rootScopeKind: 'testBoard',
);

const testCard = TestDataType(
  name: 'testCard',
  table: 'test_cards',
  field: 'text',
);

const testDataTypes = [testNote, testBoard, testCard];

/// An artificial data type whose records carry a single text field. Its
/// table is created by [createTestTables].
class TestDataType implements SyncDataType {
  const TestDataType({
    required this.name,
    required this.table,
    required this.field,
    this.rootScopeKind,
    this.maxFieldLength,
  });

  @override
  final String name;

  @override
  final String? rootScopeKind;

  final String table;
  final String field;
  final int? maxFieldLength;

  @override
  void checkLimits(SyncCommandKind kind, Map<String, Object?> fields) {
    if ((fields[field], maxFieldLength)
        case (final String value, final int maxLength)
        when value.length > maxLength) {
      throw SyncLimitExceededException(dataType: name, field: field);
    }
  }

  @override
  Future<void> writeServerRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) async {
    if (fields[field] case final String value) {
      await database.customInsert(
        'INSERT OR REPLACE INTO $table (id, scope_id, value) VALUES (?, ?, ?)',
        variables: [Variable(recordId), Variable(scopeId), Variable(value)],
      );
    } else {
      throw FormatException('$field is missing');
    }
  }

  @override
  Future<void> createRecordIfMissing(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
    required int time,
  }) => database.customInsert(
    'INSERT OR IGNORE INTO $table (id, scope_id, value) VALUES (?, ?, ?)',
    variables: [
      Variable(recordId),
      Variable(scopeId),
      Variable(fields[field] as String? ?? ''),
    ],
  );

  @override
  Future<void> updateRecordIfPresent(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
    required int time,
  }) async {
    if (fields[field] case final String value) {
      await database.customUpdate(
        'UPDATE $table SET value = ? WHERE scope_id = ? AND id = ?',
        variables: [Variable(value), Variable(scopeId), Variable(recordId)],
      );
    }
  }

  @override
  Future<void> deleteRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
  }) => database.customUpdate(
    'DELETE FROM $table WHERE scope_id = ? AND id = ?',
    variables: [Variable(scopeId), Variable(recordId)],
  );

  @override
  Future<void> deleteScopeRecords(
    AppDatabase database,
    String scopeId, {
    Set<String> keptRecordIds = const {},
  }) => database.customUpdate(
    'DELETE FROM $table WHERE scope_id = ? AND id NOT IN '
    '(SELECT value FROM json_each(?))',
    variables: [
      Variable(scopeId),
      Variable(jsonEncode([...keptRecordIds])),
    ],
  );

  @override
  Future<void> deleteAllRecords(AppDatabase database) =>
      database.customUpdate('DELETE FROM $table');

  @override
  Future<String?> scopeLabel(AppDatabase database, String scopeId) async {
    final row = await database
        .customSelect(
          'SELECT value FROM $table WHERE id = ?',
          variables: [Variable(scopeId)],
        )
        .getSingleOrNull();
    return row?.read<String>('value');
  }

  Future<Map<String, String>> readRecords(AppDatabase database) async => {
    for (final row
        in await database.customSelect('SELECT id, value FROM $table').get())
      row.read<String>('id'): row.read<String>('value'),
  };
}

/// A [TestDataType] whose operations throw the error set for them after
/// their usual effect.
class FailingTestDataType extends TestDataType {
  FailingTestDataType()
    : super(name: 'failingNote', table: 'failing_notes', field: 'text');

  Object? deleteAllRecordsError;
  Object? writeServerRecordError;

  @override
  Future<void> writeServerRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) async {
    await super.writeServerRecord(
      database,
      scopeId: scopeId,
      recordId: recordId,
      fields: fields,
    );
    if (writeServerRecordError case final error?) {
      throw error;
    }
  }

  @override
  Future<void> deleteAllRecords(AppDatabase database) async {
    await super.deleteAllRecords(database);
    if (deleteAllRecordsError case final error?) {
      throw error;
    }
  }
}

Future<void> createTestTables(
  AppDatabase database,
  List<TestDataType> dataTypes,
) async {
  for (final dataType in dataTypes) {
    await database.customStatement(
      'CREATE TABLE ${dataType.table} '
      '(id TEXT PRIMARY KEY, scope_id TEXT NOT NULL, value TEXT NOT NULL)',
    );
  }
}
