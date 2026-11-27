import 'dart:async';
import 'dart:io';

import 'package:drift_flutter/drift_flutter.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:path_provider/path_provider.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:talker/talker.dart';

import '../logging/talker_provider.dart';
import 'app_database.dart';

part 'app_database_provider.g.dart';

const _backupChannel = MethodChannel('de.murmli.app/backup');

@Riverpod(keepAlive: true)
AppDatabase appDatabase(Ref ref) {
  final talker = ref.watch(talkerProvider);
  final database = AppDatabase(
    driftDatabase(
      name: 'murmli',
      native: DriftNativeOptions(
        databaseDirectory: () => _localDataDirectory(talker),
      ),
    ),
  );
  ref.onDispose(() => unawaited(database.close()));
  return database;
}

// The data belongs to the signed-in account and must not come back through an
// OS backup on another device or after a sign-out, so the directory is kept out
// of backups.
Future<Directory> _localDataDirectory(Talker talker) async {
  final directory = Directory(
    '${(await getApplicationSupportDirectory()).path}/local-data',
  );
  await directory.create(recursive: true);
  if (defaultTargetPlatform == TargetPlatform.iOS) {
    try {
      await _backupChannel.invokeMethod<void>(
        'excludeFromBackup',
        directory.path,
      );
    } on PlatformException catch (exception, stackTrace) {
      talker.warning(
        'Excluding the local data from backups failed',
        exception,
        stackTrace,
      );
    } on MissingPluginException catch (exception, stackTrace) {
      talker.warning(
        'Excluding the local data from backups failed',
        exception,
        stackTrace,
      );
    }
  }
  return directory;
}
