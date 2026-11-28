import 'dart:async';

import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../authentication/signed_in_account_provider.dart';
import 'app_database.dart';
import 'app_database_provider.dart';

part 'signed_out_data_wipe_provider.g.dart';

@Riverpod(keepAlive: true)
void signedOutDataWipe(Ref ref) {
  ref.listen(signedInAccountProvider, (previous, account) {
    if (account == null) {
      unawaited(_deleteAllRows(ref.read(appDatabaseProvider)));
    }
  }, fireImmediately: true);
}

Future<void> _deleteAllRows(AppDatabase database) =>
    database.transaction(() async {
      for (final table in database.allTables) {
        await database.delete(table).go();
      }
    });
