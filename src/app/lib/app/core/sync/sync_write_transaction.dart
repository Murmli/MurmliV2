import 'sync_data_type.dart';

/// Changes local data and queues the matching commands. Every call checks the
/// limits of the data type first and throws [SyncLimitExceededException]
/// before anything changes.
abstract interface class SyncWriteTransaction {
  Future<void> create({
    required SyncDataType dataType,
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  });

  Future<void> setFields({
    required SyncDataType dataType,
    required String scopeId,
    required String recordId,
    required Map<String, Object?> fields,
  });

  Future<void> delete({
    required SyncDataType dataType,
    required String scopeId,
    required String recordId,
  });
}
