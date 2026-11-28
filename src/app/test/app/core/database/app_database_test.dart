import 'package:app/app/core/database/app_database.dart';
import 'package:drift/native.dart';
import 'package:drift_dev/api/migrations_native.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../../drift/app_database/generated/schema.dart';

void main() {
  late SchemaVerifier verifier;

  setUp(() {
    verifier = SchemaVerifier(GeneratedHelper());
  });

  test('a new database has the schema of the latest dump', () async {
    final database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);

    expect(database.schemaVersion, GeneratedHelper.versions.last);
    await verifier.migrateAndValidate(database, database.schemaVersion);
  });

  test('an opened database enforces foreign keys', () async {
    final database = AppDatabase(NativeDatabase.memory());
    addTearDown(database.close);

    final foreignKeys = await database
        .customSelect('PRAGMA foreign_keys')
        .getSingle();

    expect(foreignKeys.read<int>('foreign_keys'), 1);
  });

  test('an upgrade from version 2 discards the sync token and increases the '
      'generation', () async {
    final schema = await verifier.schemaAt(2);
    schema.rawDatabase.execute(
      'INSERT INTO sync_states (id, token, local_state_generation) '
      "VALUES (1, 'token', 3)",
    );
    final database = AppDatabase(schema.newConnection());
    addTearDown(database.close);

    await verifier.migrateAndValidate(database, database.schemaVersion);

    final state = await database.select(database.syncStates).getSingle();
    expect((state.token, state.localStateGeneration), (null, 4));
  });

  test('a downgrade recreates all tables empty', () async {
    final schema = await verifier.schemaAt(GeneratedHelper.versions.last);
    schema.rawDatabase
      ..execute(
        "INSERT INTO shopping_lists (id, name) VALUES ('list', 'Groceries')",
      )
      ..execute(
        'INSERT INTO sync_states (id, token, local_state_generation) '
        "VALUES (1, 'token', 3)",
      )
      ..execute('CREATE TABLE newer_records (id TEXT NOT NULL)')
      ..userVersion = GeneratedHelper.versions.last + 1;
    final database = AppDatabase(schema.newConnection());
    addTearDown(database.close);

    await verifier.migrateAndValidate(
      database,
      database.schemaVersion,
      options: const ValidationOptions(validateDropped: true),
    );

    expect(await database.select(database.shoppingLists).get(), isEmpty);
    expect(await database.select(database.syncStates).get(), isEmpty);
  });
}
