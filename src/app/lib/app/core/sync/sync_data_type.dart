import '../database/app_database.dart';
import 'sync_command_kind.dart';

abstract interface class SyncDataType {
  String get name;

  /// The kind of shared scope whose root record this type is, `null` for
  /// every other type. A root record has the id of its scope.
  String? get rootScopeKind;

  /// Throws [SyncLimitExceededException] for values the server never accepts.
  void checkLimits(SyncCommandKind kind, Map<String, Object?> fields);

  /// Inserts or replaces the record with the complete state from the server.
  /// Ignores fields and values this version does not know and throws a
  /// [FormatException] for a record it cannot read.
  Future<void> writeServerRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  });

  Future<void> createRecordIfMissing(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  });

  /// Changes only the record with [recordId] in [scopeId].
  Future<void> updateRecordIfPresent(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  });

  /// Deletes only the record with [recordId] in [scopeId].
  Future<void> deleteRecord(
    AppDatabase database, {
    required String scopeId,
    required String recordId,
  });

  Future<void> deleteScopeRecords(AppDatabase database, String scopeId);

  Future<void> deleteAllRecords(AppDatabase database);

  /// The label of the scope in notices, such as the name of a list.
  Future<String?> scopeLabel(AppDatabase database, String scopeId);
}

class SyncLimitExceededException implements Exception {
  const SyncLimitExceededException({
    required this.dataType,
    required this.field,
  });

  final String dataType;
  final String field;

  @override
  String toString() =>
      'SyncLimitExceededException: $field of $dataType exceeds its limit';
}
