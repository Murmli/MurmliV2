import 'package:drift/drift.dart' show Value;
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/database/app_database.dart';
import 'package:app/app/core/sync/sync_core.dart';
import 'package:app/app/core/sync/sync_round_outcome.dart';

import 'sync_test_setup.dart';
import 'test_data_types.dart';

const _boardId = 'board-scope';

void main() {
  late SyncTestHarness harness;

  setUp(() async {
    harness = await SyncTestHarness.start();
  });

  Future<void> fillLocalData() async {
    await harness.completeFirstRound(
      records: [serverRecord(testNote, 'server-note', 'from server')],
      scopes: [
        scopeBlock(
          _boardId,
          kind: 'testBoard',
          fullState: true,
          records: [
            serverRecord(testBoard, _boardId, 'Groceries'),
            serverRecord(testCard, 'card-1', 'Milk'),
          ],
          members: [serverMember('own-member', isOwn: true)],
        ),
      ],
    );
    await harness.create(testNote, 'rejected-note', 'rejected');
    final [command] = await harness.readCommands();
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        results: [commandResult(command.id, 'rejected', reason: 'invalid')],
      ),
    );
    await harness.core.runRound();
    await harness.create(testNote, 'queued-note', 'queued');
  }

  Future<void> expectNoLocalData() async {
    expect(await harness.readCommands(), isEmpty);
    expect(await harness.readScopeIds(), isEmpty);
    expect(
      await harness.database.select(harness.database.syncMembers).get(),
      isEmpty,
    );
    expect(await harness.core.watchNotices().first, isEmpty);
    for (final dataType in testDataTypes) {
      expect(await dataType.readRecords(harness.database), isEmpty);
    }
  }

  test(
    'adoptAccount for another account leaves nothing of the previous one',
    () async {
      await fillLocalData();
      final previousDeviceId = (await harness.readState()).deviceId;

      await harness.core.adoptAccount('other-account');

      await expectNoLocalData();
      final state = await harness.readState();
      expect(state.accountId, 'other-account');
      expect(state.token, isNull);
      expect(state.deviceId, allOf(isNotNull, isNot(previousDeviceId)));
      await expectLater(
        harness.create(testNote, 'note-a', 'a'),
        throwsA(isA<SyncNotReadyException>()),
      );
    },
  );

  group('syncCoreProvider', () {
    void signIn(Account? account) =>
        harness.container.read(signedInAccountProvider.notifier).state =
            account;

    test('after signing in with another account leaves nothing of the '
        'previous one', () async {
      await fillLocalData();

      signIn(const Account(id: 'other-account'));
      await pumpEventQueue();

      await expectNoLocalData();
      expect((await harness.readState()).accountId, 'other-account');
    });

    test('after signing out wipes all local data including the device '
        'id', () async {
      await fillLocalData();

      signIn(null);
      await pumpEventQueue();

      await expectNoLocalData();
      final state = await harness.readState();
      expect((state.accountId, state.deviceId), (null, null));
    });
  });

  test('adoptAccount for another account restarts the count of failed '
      'applications', () async {
    await harness.completeFirstRound();
    void answerUnappliable() => harness.server.answer(
      (_) => harness.response(
        token: 'unappliable-token',
        scopes: [
          scopeBlock(
            personalScopeId,
            members: [serverMember('member'), serverMember('member')],
          ),
        ],
      ),
    );
    answerUnappliable();
    answerUnappliable();
    await harness.core.runRound();
    await harness.core.runRound();
    await harness.core.adoptAccount('other-account');
    answerUnappliable();

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.failed());
    expect(harness.crashReporter.calls, isEmpty);
  });

  test('runRound started together with a change of the account sends nothing '
      'of the previous account', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    final previousDeviceId = (await harness.readState()).deviceId;
    harness.server.answer((_) => harness.response(token: 'other-token'));

    final accountChange = harness.core.adoptAccount('other-account');
    final round = harness.core.runRound();
    await accountChange;
    await round;

    final request = harness.server.requests.last;
    final deviceId = (await harness.readState()).deviceId;
    expect(deviceId, isNot(previousDeviceId));
    expect(request.headers['Murmli-Device-Id'], deviceId);
    expect(sentCommandIds(request.data as RequestBody), isEmpty);
  });

  group('with a data type that cannot delete its records', () {
    late FailingTestDataType failingNote;

    setUp(() async {
      failingNote = FailingTestDataType();
      harness = await SyncTestHarness.start(
        dataTypes: [...testDataTypes, failingNote],
      );
    });

    test('adoptAccount whose wipe fails leaves the queue of the previous '
        'account unsent and refuses writes', () async {
      await harness.completeFirstRound();
      await harness.create(testNote, 'note-a', 'a');
      failingNote.deleteAllRecordsError = Exception('wipe failed');

      await expectLater(
        harness.core.adoptAccount('other-account'),
        throwsException,
      );
      final outcome = await harness.core.runRound();

      expect(outcome, const SyncRoundOutcome.notSignedIn());
      expect(harness.server.requests, hasLength(1));
      await expectLater(
        harness.create(testNote, 'note-b', 'b'),
        throwsA(isA<SyncNotReadyException>()),
      );
    });

    test('runRound answered with account-gone whose wipe throws an error ends '
        'as failed and reports it', () async {
      await harness.completeFirstRound();
      failingNote.deleteAllRecordsError = StateError('wipe failed');
      harness.server.answerProblem(410, code: 'account-gone');

      final outcome = await harness.core.runRound();

      expect(outcome, const SyncRoundOutcome.failed());
      expect(harness.crashReporter.calls, [
        'problem A sync round failed unexpectedly',
      ]);
    });
  });

  test('runRound answered with account-gone wipes all local data', () async {
    await fillLocalData();
    harness.server.answerProblem(410, code: 'account-gone');

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.accountGone());
    await expectNoLocalData();
    final state = await harness.readState();
    expect((state.accountId, state.deviceId, state.token), (null, null, null));
  });

  group('runRound with a response to an outdated state', () {
    setUp(() async {
      await harness.completeFirstRound(
        records: [serverRecord(testNote, 'server-note', 'original')],
      );
      await harness.create(testNote, 'local-note', 'local');
    });

    HeldResponse holdResponseChangingEverything() =>
        harness.server.answerAfterRelease(
          (requestBody) => harness.response(
            token: 'second-token',
            coversAllScopes: true,
            results: [
              for (final commandId in sentCommandIds(requestBody))
                commandResult(commandId, 'applied'),
            ],
            scopes: [
              scopeBlock(
                personalScopeId,
                fullState: true,
                records: [serverRecord(testNote, 'server-note', 'changed')],
              ),
            ],
          ),
        );

    test('after a sign-out during the round applies nothing', () async {
      final held = holdResponseChangingEverything();

      final round = harness.core.runRound();
      await held.arrived;
      await harness.core.adoptAccount(null);
      final afterSignOut = await harness.snapshot();
      held.release();
      await round;

      expect(await harness.snapshot(), afterSignOut);
      await expectNoLocalData();
    });

    test('after the token changed during the round applies nothing', () async {
      final held = holdResponseChangingEverything();

      final round = harness.core.runRound();
      await held.arrived;
      await harness.database
          .update(harness.database.syncStates)
          .write(
            const SyncStatesCompanion(token: Value('token-of-another-round')),
          );
      final afterTokenChange = await harness.snapshot();
      held.release();
      await round;

      expect(await harness.snapshot(), afterTokenChange);
    });

    test('answered with account-gone after a change of the account keeps '
        'the new account', () async {
      final held = harness.server.answerAfterRelease(
        (_) => {'type': 'tag:murmli.de,2026:account-gone', 'status': 410},
        status: 410,
      );

      final round = harness.core.runRound();
      await held.arrived;
      await harness.core.adoptAccount('other-account');
      final afterAccountChange = await harness.snapshot();
      held.release();

      expect(await round, roundOutcome);
      expect(await harness.snapshot(), afterAccountChange);
    });

    test('answered with request-too-large after the generation changed '
        'changes nothing and sends the full portion next', () async {
      await harness.create(testNote, 'other-note', 'other');
      final queuedIds = [
        for (final command in await harness.readCommands()) command.id,
      ];
      final held = harness.server.answerAfterRelease(
        (_) => {
          'type': 'tag:murmli.de,2026:request-too-large',
          'status': 413,
          'maxRequestBytes': 100000,
          'maxCommandsPerRequest': 7,
        },
        status: 413,
      );
      harness.answerApplyingAll(token: 'second-token');

      final round = harness.core.runRound();
      await held.arrived;
      await harness.core.discardToken();
      final afterDiscard = await harness.snapshot();
      held.release();

      expect(await round, roundOutcome);
      expect(await harness.snapshot(), afterDiscard);
      await harness.core.runRound();
      expect(sentCommandIds(harness.server.requestBodies.last), queuedIds);
    });

    test('after the generation changed during the round applies '
        'nothing', () async {
      final held = holdResponseChangingEverything();

      final round = harness.core.runRound();
      await held.arrived;
      await harness.core.discardToken();
      final afterDiscard = await harness.snapshot();
      held.release();
      await round;

      expect(await harness.snapshot(), afterDiscard);
      expect(await harness.readCommands(), hasLength(1));
    });
  });
}
