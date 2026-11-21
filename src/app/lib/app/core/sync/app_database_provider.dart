import 'package:drift_sqlite_async/drift_sqlite_async.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'app_database.dart';
import 'power_sync_database_provider.dart';

part 'app_database_provider.g.dart';

@Riverpod(keepAlive: true)
Future<AppDatabase> appDatabase(Ref ref) async {
  final powerSyncDatabase = await ref.watch(powerSyncDatabaseProvider.future);
  final database = AppDatabase(SqliteAsyncDriftConnection(powerSyncDatabase));
  ref.onDispose(database.close);
  return database;
}
