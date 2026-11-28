import 'package:drift/drift.dart';

class SyncCommands extends Table {
  IntColumn get sequence => integer().autoIncrement()();
  TextColumn get id => text().unique()();
  TextColumn get kind => text()();
  TextColumn get scopeId => text()();
  TextColumn get dataType => text()();
  TextColumn get recordId => text()();
  TextColumn get fields => text().nullable()();
  IntColumn get time => integer()();
  IntColumn get failedAttempts => integer().withDefault(const Constant(0))();
  IntColumn get firstFailedAt => integer().nullable()();
}

class SyncStates extends Table {
  IntColumn get id => integer()();
  TextColumn get accountId => text().nullable()();
  TextColumn get deviceId => text().nullable()();
  TextColumn get token => text().nullable()();
  IntColumn get lastCommandTime => integer().nullable()();
  IntColumn get clockOffsetMilliseconds =>
      integer().withDefault(const Constant(0))();
  IntColumn get pollIntervalSeconds => integer().nullable()();
  IntColumn get maxCommandsPerRequest => integer().nullable()();
  IntColumn get maxRequestBytes => integer().nullable()();
  IntColumn get localStateGeneration =>
      integer().withDefault(const Constant(0))();
  BoolColumn get firstSyncCompleted =>
      boolean().withDefault(const Constant(false))();

  @override
  Set<Column<Object>> get primaryKey => {id};

  @override
  List<String> get customConstraints => ['CHECK (id = 1)'];
}

class SyncScopes extends Table {
  TextColumn get scopeId => text()();
  TextColumn get kind => text()();

  @override
  Set<Column<Object>> get primaryKey => {scopeId};
}

class SyncMembers extends Table {
  TextColumn get scopeId => text()();
  TextColumn get memberId => text()();
  TextColumn get name => text()();
  TextColumn get role => text()();
  TextColumn get state => text()();
  BoolColumn get isOwn => boolean()();

  @override
  Set<Column<Object>> get primaryKey => {scopeId, memberId};
}

@DataClassName('SyncNoticeRow')
class SyncNotices extends Table {
  IntColumn get id => integer().autoIncrement()();
  TextColumn get kind => text()();
  TextColumn get scopeId => text()();
  TextColumn get dataType => text().nullable()();
  TextColumn get recordId => text().nullable()();
  TextColumn get commandKind => text().nullable()();
  TextColumn get reason => text().nullable()();
  TextColumn get label => text().nullable()();
  IntColumn get createdAt => integer()();
}
