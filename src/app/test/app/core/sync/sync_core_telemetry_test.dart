import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/logging/talker_provider.dart';
import 'package:app/app/core/sync/sync_round_outcome.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/http_client_adapter.dart';
import 'sync_test_setup.dart';
import 'test_data_types.dart';

void main() {
  late SyncTestHarness harness;
  late Talker talker;

  setUp(() async {
    harness = await SyncTestHarness.start();
    talker = harness.container.read(talkerProvider);
  });

  List<String?> roundLogs() => [
    for (final log in talker.history)
      if (log.message?.startsWith('Sync round ') ?? false) log.message,
  ];

  test('runRound logs one entry with its duration, the number of sent '
      'commands, the results by status and the trace id', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    await harness.create(testNote, 'note-b', 'b');
    await harness.create(testNote, 'note-c', 'c');
    talker.cleanHistory();
    harness.server.answer((requestBody) {
      harness.now = harness.now.add(const Duration(milliseconds: 1250));
      final [noteA, noteB, noteC] = sentCommandIds(requestBody);
      return harness.response(
        token: 'second-token',
        results: [
          commandResult(noteA, 'applied'),
          commandResult(noteB, 'rejected', reason: 'noAccess'),
          commandResult(noteC, 'applied'),
        ],
      );
    });

    await harness.core.runRound();

    expect(roundLogs(), [
      'Sync round completed in 1250 ms with 3 sent commands, results: '
          'applied 2, rejected 1, '
          'trace id ${sentTraceId(harness.server.requests.last)}',
    ]);
  });

  test('runRound failing for lack of network logs the failed round and '
      'reports nothing to Sentry', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    talker.cleanHistory();
    harness.server.failWith(const SocketException('offline'));

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.failed());
    expect(roundLogs(), [
      'Sync round failed in 0 ms with 1 sent commands, results: none, '
          'trace id ${sentTraceId(harness.server.requests.last)}',
    ]);
    expect(harness.crashReporter.calls, isEmpty);
  });

  test('runRound without the signed-in account logs the round without a '
      'request', () async {
    await harness.core.adoptAccount(null);
    talker.cleanHistory();

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.notSignedIn());
    expect(roundLogs(), [
      'Sync round notSignedIn in 0 ms with 0 sent commands, results: none, '
          'trace id none',
    ]);
  });

  test('runRound writes no field values, member names or tokens to the log '
      'or to Sentry', () async {
    await harness.completeFirstRound(
      records: [serverRecord(testNote, 'server-note', 'server secret')],
      scopes: [
        scopeBlock(
          'shared-scope',
          kind: 'testBoard',
          fullState: true,
          members: [serverMember('member', name: 'Secret Member')],
        ),
      ],
    );
    await harness.create(testNote, 'local-note', 'local secret');
    final [command] = await harness.readCommands();
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        results: [commandResult(command.id, 'rejected', reason: 'noAccess')],
        scopes: [
          scopeBlock(
            personalScopeId,
            records: [
              {
                'dataType': 'testNote',
                'id': 'unreadable-note',
                'fields': {
                  'text': ['unreadable secret'],
                },
              },
            ],
          ),
        ],
      ),
    );

    await harness.core.runRound();

    final reported = [
      for (final log in talker.history) log.generateTextMessage(),
      ...harness.crashReporter.calls,
    ];
    expect(reported, isNotEmpty);
    for (final secret in [
      'server secret',
      'local secret',
      'unreadable secret',
      'Secret Member',
      firstToken,
      'second-token',
      signedSessionToken,
      issuedJsonWebToken,
    ]) {
      expect(reported, everyElement(isNot(contains(secret))));
    }
  });

  test('the request reports how many commands wait, including those it does '
      'not carry, and the age of the oldest one', () async {
    harness.server.answer(
      (_) => {
        ...harness.response(
          token: firstToken,
          coversAllScopes: true,
          scopes: [scopeBlock(personalScopeId, fullState: true)],
        ),
        'settings': {'maxCommandsPerRequest': 1},
      },
    );
    await harness.core.runRound();
    await harness.create(testNote, 'note-a', 'a');
    harness.now = harness.now.add(const Duration(seconds: 90));
    await harness.create(testNote, 'note-b', 'b');
    harness.now = harness.now.add(const Duration(seconds: 30));
    harness.answerApplyingAll(token: 'second-token');

    await harness.core.runRound();

    final firstRequest = harness.server.requestBodies.first;
    expect(
      (
        firstRequest['pendingCommandCount'],
        firstRequest.containsKey('oldestPendingCommandAgeSeconds'),
      ),
      (0, false),
    );
    final lastRequest = harness.server.requestBodies.last;
    expect(lastRequest['commands'], hasLength(1));
    expect(
      (
        lastRequest['pendingCommandCount'],
        lastRequest['oldestPendingCommandAgeSeconds'],
      ),
      (2, 120),
    );
  });
}
