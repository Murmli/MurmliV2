import 'package:path/path.dart';
import 'package:path_provider/path_provider.dart';
import 'package:powersync/powersync.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'sync_schema.dart';

part 'power_sync_database_provider.g.dart';

@Riverpod(keepAlive: true)
Future<PowerSyncDatabase> powerSyncDatabase(Ref ref) async {
  final directory = await getApplicationSupportDirectory();
  final database = PowerSyncDatabase(
    schema: syncSchema,
    path: join(directory.path, 'murmli.db'),
  );
  ref.onDispose(database.close);
  await database.initialize();
  return database;
}
