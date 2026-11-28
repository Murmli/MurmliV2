import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/sync/sync_core.dart';
import 'package:app/app/core/sync/sync_data_type.dart';

import 'sync_test_setup.dart';
import 'test_data_types.dart';

void main() {
  late SyncTestHarness harness;

  setUp(() async {
    harness = await SyncTestHarness.start();
  });

  Future<List<int>> commandTimes() async => [
    for (final command in await harness.readCommands()) command.time,
  ];

  test(
    'write before the first completed round throws SyncNotReadyException',
    () async {
      harness.server.answerProblem(500);
      await harness.core.runRound();

      await expectLater(
        harness.create(testNote, 'note-a', 'a'),
        throwsA(isA<SyncNotReadyException>()),
      );
      expect(await harness.readCommands(), isEmpty);
    },
  );

  test('watchFirstSyncCompleted is false until the first completed round and '
      'again after signing out', () async {
    final readiness = expectLater(
      harness.core.watchFirstSyncCompleted(),
      emitsInOrder([false, true]),
    );

    await harness.completeFirstRound();
    await readiness;
    await harness.core.adoptAccount(null);

    expect(await harness.core.watchFirstSyncCompleted().first, isFalse);
  });

  group('write after the first completed round', () {
    setUp(() async {
      await harness.completeFirstRound(
        records: [serverRecord(testNote, 'server-note', 'original')],
      );
    });

    test('with an action that throws midway stores neither data nor '
        'command', () async {
      await expectLater(
        harness.core.write((transaction) async {
          await transaction.create(
            dataType: testNote,
            scopeId: personalScopeId,
            recordId: 'note-a',
            fields: {'text': 'a'},
          );
          throw StateError('action failed');
        }),
        throwsStateError,
      );

      expect(await harness.readCommands(), isEmpty);
      expect(await testNote.readRecords(harness.database), {
        'server-note': 'original',
      });
    });

    test('with a later change over a limit stores neither the earlier change '
        'nor any command', () async {
      await expectLater(
        harness.core.write((transaction) async {
          await transaction.setFields(
            dataType: testNote,
            scopeId: personalScopeId,
            recordId: 'server-note',
            fields: {'text': 'changed'},
          );
          await transaction.create(
            dataType: testNote,
            scopeId: personalScopeId,
            recordId: 'note-a',
            fields: {'text': 'x' * (testNoteMaxTextLength + 1)},
          );
        }),
        throwsA(isA<SyncLimitExceededException>()),
      );

      expect(await harness.readCommands(), isEmpty);
      expect(await testNote.readRecords(harness.database), {
        'server-note': 'original',
      });
    });

    test('with a value over a limit creates no command', () async {
      await expectLater(
        harness.setField(
          testNote,
          'server-note',
          'x' * (testNoteMaxTextLength + 1),
        ),
        throwsA(isA<SyncLimitExceededException>()),
      );

      expect(await harness.readCommands(), isEmpty);
      expect(await testNote.readRecords(harness.database), {
        'server-note': 'original',
      });
    });

    test('after a small backward clock jump keeps the time of the previous '
        'command and after a large one follows the clock', () async {
      final start = harness.now;

      await harness.create(testNote, 'note-a', 'a');
      harness.now = start.subtract(const Duration(minutes: 2));
      await harness.create(testNote, 'note-b', 'b');
      harness.now = start.subtract(const Duration(minutes: 10));
      await harness.create(testNote, 'note-c', 'c');

      expect(await commandTimes(), [
        start.millisecondsSinceEpoch,
        start.millisecondsSinceEpoch,
        start.subtract(const Duration(minutes: 10)).millisecondsSinceEpoch,
      ]);
    });

    test('corrects command times by the clock offset to the server', () async {
      harness.server.answer(
        (_) => harness.response(
          token: 'second-token',
          serverTime: harness.now.add(const Duration(seconds: 30)),
        ),
      );
      await harness.core.runRound();

      await harness.create(testNote, 'note-a', 'a');

      expect(await commandTimes(), [
        harness.now.add(const Duration(seconds: 30)).millisecondsSinceEpoch,
      ]);
    });

    test('notifies commandsQueued', () async {
      final queued = expectLater(harness.core.commandsQueued, emits(null));

      await harness.create(testNote, 'note-a', 'a');

      await queued;
    });

    test('marks the record as pending and counts its command until the server '
        'confirmed it', () async {
      final pending = expectLater(
        harness.core.watchHasPendingCommands('note-a'),
        emitsInOrder([false, true]),
      );
      await harness.create(testNote, 'note-a', 'a');
      await harness.create(testNote, 'note-b', 'b');
      await pending;
      final pendingCountBeforeRound = await harness.core.pendingCommandCount();
      harness.answerApplyingAll(token: 'second-token');

      await harness.core.runRound();

      expect(pendingCountBeforeRound, 2);
      expect(await harness.core.pendingCommandCount(), 0);
      expect(
        await harness.core.watchHasPendingCommands('note-a').first,
        isFalse,
      );
    });
  });
}
