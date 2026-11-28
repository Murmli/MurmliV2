import 'package:drift/drift.dart';

import '../../../core/database/app_database.dart';
import '../../../core/sync/sync_command_kind.dart';
import '../../../core/sync/sync_data_type.dart';
import 'sync_field_reading.dart';

const _maxNameLength = 100;

/// The root record of a shopping list scope, its id is the id of the scope.
class ShoppingListDataType implements SyncDataType {
  const ShoppingListDataType();

  @override
  String get name => 'shoppingList';

  @override
  String? get rootScopeKind => 'shoppingList';

  @override
  void checkLimits(SyncCommandKind kind, Map<String, Object?> fields) {
    final listName = fields['name'];
    if ((kind == SyncCommandKind.create || fields.containsKey('name')) &&
        (listName is! String || listName.length > _maxNameLength)) {
      throw SyncLimitExceededException(dataType: name, field: 'name');
    }
  }

  @override
  Future<void> writeServerRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  }) async {
    if (recordId != scopeId) {
      throw const FormatException('the id differs from the scope');
    }
    await database
        .into(database.shoppingLists)
        .insertOnConflictUpdate(
          ShoppingListsCompanion.insert(
            id: recordId,
            name: readRequiredField<String>(fields, 'name'),
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
      .into(database.shoppingLists)
      .insert(
        ShoppingListsCompanion.insert(
          id: recordId,
          name: fields['name'] as String? ?? '',
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
    if (recordId != scopeId || fields['name'] is! String) {
      return;
    }
    await (database.update(database.shoppingLists)
          ..where((list) => list.id.equals(recordId)))
        .write(ShoppingListsCompanion(name: Value(fields['name']! as String)));
  }

  @override
  Future<void> deleteRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
  }) async {
    if (recordId == scopeId) {
      await deleteScopeRecords(database, scopeId);
    }
  }

  @override
  Future<void> deleteScopeRecords(
    AppDatabase database,
    String scopeId, {
    Set<String> keptRecordIds = const {},
  }) =>
      (database.delete(database.shoppingLists)..where(
            (list) => list.id.equals(scopeId) & list.id.isNotIn(keptRecordIds),
          ))
          .go();

  @override
  Future<void> deleteAllRecords(AppDatabase database) =>
      database.delete(database.shoppingLists).go();

  @override
  Future<String?> scopeLabel(AppDatabase database, String scopeId) async {
    final list = await (database.select(
      database.shoppingLists,
    )..where((list) => list.id.equals(scopeId))).getSingleOrNull();
    return switch (list?.name) {
      final listName? when listName.isNotEmpty => listName,
      _ => null,
    };
  }
}
