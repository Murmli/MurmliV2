import 'package:powersync/powersync.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'power_sync_database_provider.dart';

part 'sync_status_provider.g.dart';

@Riverpod(keepAlive: true)
Stream<SyncStatus> syncStatus(Ref ref) async* {
  final database = await ref.watch(powerSyncDatabaseProvider.future);
  yield database.currentStatus;
  yield* database.statusStream;
}
