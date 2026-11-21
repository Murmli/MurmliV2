import 'package:drift_flutter/drift_flutter.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'app_database.dart';

part 'app_database_provider.g.dart';

@Riverpod(keepAlive: true)
Future<AppDatabase> appDatabase(Ref ref) async {
  final database = AppDatabase(driftDatabase(name: 'murmli'));
  ref.onDispose(database.close);
  // drift opens lazily, opening here turns a failure into this provider's
  // error instead of one in every query.
  await database.customSelect('SELECT 1').get();
  return database;
}
