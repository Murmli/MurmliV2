import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/sync/sync_notice.dart';

import 'sync_test_setup.dart';
import 'test_data_types.dart';

const _boardId = 'board-scope';

void main() {
  late SyncTestHarness harness;

  setUp(() async {
    harness = await SyncTestHarness.start();
  });

  Map<String, Object?> boardBlock({bool fullState = true}) => scopeBlock(
    _boardId,
    kind: 'testBoard',
    fullState: fullState,
    records: [
      serverRecord(testBoard, _boardId, 'Groceries'),
      serverRecord(testCard, 'card-1', 'Milk'),
      serverRecord(testCard, 'card-2', 'Bread'),
    ],
    members: [
      serverMember('own-member', name: 'Me', isOwn: true),
      serverMember('other-member', name: 'Other'),
    ],
  );

  Future<List<String>> memberIdsOfBoard() async => [
    for (final member in await harness.core.watchMembers(_boardId).first)
      member.memberId,
  ];

  test('runRound with a full state replaces the records of the scope and keeps '
      'the effects of queued commands', () async {
    await harness.completeFirstRound(
      records: [
        serverRecord(testNote, 'note-1', 'one'),
        serverRecord(testNote, 'note-2', 'two'),
      ],
    );
    await harness.setField(testNote, 'note-1', 'edited locally');
    await harness.create(testNote, 'local-note', 'local');
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        scopes: [
          scopeBlock(
            personalScopeId,
            fullState: true,
            records: [
              serverRecord(testNote, 'note-1', 'changed on server'),
              serverRecord(testNote, 'note-3', 'three'),
            ],
          ),
        ],
      ),
    );

    await harness.core.runRound();

    expect(await testNote.readRecords(harness.database), {
      'note-1': 'edited locally',
      'note-3': 'three',
      'local-note': 'local',
    });
    expect(await harness.readCommands(), hasLength(2));
  });

  test('runRound applies a deleted record only to the record of that id in its '
      'scope', () async {
    await harness.completeFirstRound(
      records: [serverRecord(testCard, 'card-1', 'Personal card')],
      scopes: [scopeBlock(_boardId, kind: 'testBoard', fullState: true)],
    );
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        scopes: [
          scopeBlock(
            _boardId,
            kind: 'testBoard',
            deletedRecords: [
              {'dataType': 'testCard', 'id': 'card-1'},
            ],
          ),
        ],
      ),
    );

    await harness.core.runRound();

    expect(await testCard.readRecords(harness.database), {
      'card-1': 'Personal card',
    });
  });

  test('runRound with a response covering all scopes removes every other local '
      'scope', () async {
    await harness.completeFirstRound(scopes: [boardBlock()]);
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        coversAllScopes: true,
        scopes: [scopeBlock(personalScopeId)],
      ),
    );

    await harness.core.runRound();

    expect(await harness.readScopeIds(), [personalScopeId]);
    expect(await testBoard.readRecords(harness.database), isEmpty);
    expect(await testCard.readRecords(harness.database), isEmpty);
    expect(await memberIdsOfBoard(), isEmpty);
  });

  group('runRound with a removed scope', () {
    setUp(() async {
      await harness.completeFirstRound(scopes: [boardBlock()]);
      await harness.setField(testCard, 'card-1', 'Oat milk', scopeId: _boardId);
      await harness.create(testNote, 'note-a', 'a');
    });

    void answerRemovedBoard({required bool removedByOwnAccount}) =>
        harness.server.answer(
          (_) => harness.response(
            token: 'second-token',
            removedScopes: [
              {'scopeId': _boardId, 'removedByOwnAccount': removedByOwnAccount},
            ],
          ),
        );

    test('removes it with its queued commands and leaves a notice with its '
        'label', () async {
      answerRemovedBoard(removedByOwnAccount: false);

      await harness.core.runRound();

      expect(await harness.readScopeIds(), [personalScopeId]);
      expect(await testBoard.readRecords(harness.database), isEmpty);
      expect(await testCard.readRecords(harness.database), isEmpty);
      expect(await memberIdsOfBoard(), isEmpty);
      expect(
        [for (final command in await harness.readCommands()) command.recordId],
        ['note-a'],
      );
      final [notice] = await harness.core.watchNotices().first;
      expect(
        (notice.kind, notice.scopeId, notice.label),
        (SyncNoticeKind.removedScope, _boardId, 'Groceries'),
      );
    });

    test('removed by the own account leaves no notice', () async {
      answerRemovedBoard(removedByOwnAccount: true);

      await harness.core.runRound();

      expect(await harness.readScopeIds(), [personalScopeId]);
      expect(await testCard.readRecords(harness.database), isEmpty);
      expect(await harness.core.watchNotices().first, isEmpty);
    });
  });

  test('runRound after a board was created, filled and deleted offline leaves '
      'nothing of it on the device', () async {
    await harness.completeFirstRound();
    await harness.create(testBoard, _boardId, 'Party', scopeId: _boardId);
    await harness.create(testCard, 'card-1', 'Cake', scopeId: _boardId);
    await harness.create(testCard, 'card-2', 'Juice', scopeId: _boardId);
    await harness.delete(testBoard, _boardId, scopeId: _boardId);
    harness.server.answer(
      (requestBody) => harness.response(
        token: 'second-token',
        results: [
          for (final commandId in sentCommandIds(requestBody))
            commandResult(commandId, 'applied'),
        ],
        removedScopes: [
          {'scopeId': _boardId, 'removedByOwnAccount': true},
        ],
      ),
    );

    await harness.core.runRound();

    expect(sentCommandIds(harness.server.requestBodies.last), hasLength(4));
    expect(await harness.readScopeIds(), [personalScopeId]);
    expect(await testBoard.readRecords(harness.database), isEmpty);
    expect(await testCard.readRecords(harness.database), isEmpty);
    expect(await harness.readCommands(), isEmpty);
    expect(await harness.core.watchNotices().first, isEmpty);
  });

  test(
    'runRound after the server rejects a board deleted offline brings it back '
    'with all cards and members',
    () async {
      await harness.completeFirstRound(scopes: [boardBlock()]);
      await harness.delete(testBoard, _boardId, scopeId: _boardId);
      final boardsAfterDelete = await testBoard.readRecords(harness.database);
      final [command] = await harness.readCommands();
      harness.server.answer(
        (_) => harness.response(
          token: 'second-token',
          results: [commandResult(command.id, 'rejected', reason: 'noAccess')],
          scopes: [boardBlock()],
        ),
      );

      await harness.core.runRound();

      expect(boardsAfterDelete, isEmpty);
      expect(await testBoard.readRecords(harness.database), {
        _boardId: 'Groceries',
      });
      expect(await testCard.readRecords(harness.database), {
        'card-1': 'Milk',
        'card-2': 'Bread',
      });
      expect(await memberIdsOfBoard(), ['other-member', 'own-member']);
      final [notice] = await harness.core.watchNotices().first;
      expect(
        (notice.kind, notice.reason),
        (SyncNoticeKind.rejectedCommand, 'noAccess'),
      );
    },
  );
}
