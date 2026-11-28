import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/api/failed_api_request_trace_id_provider.dart';
import 'package:app/app/core/sync/sync_notice.dart';
import 'package:app/app/core/sync/sync_round_outcome.dart';

import 'sync_test_setup.dart';
import 'test_data_types.dart';

void main() {
  late SyncTestHarness harness;

  setUp(() async {
    harness = await SyncTestHarness.start();
  });

  Future<List<String>> queuedCommandIds() async => [
    for (final command in await harness.readCommands()) command.id,
  ];

  test('runRound sends the device id, no token at first and then the queued '
      'commands in their wire format', () async {
    await harness.completeFirstRound(
      records: [serverRecord(testNote, 'server-note', 'from server')],
    );
    await harness.create(testNote, 'local-note', 'local');
    harness.answerApplyingAll(token: 'second-token');

    final outcome = await harness.core.runRound();

    expect(outcome, roundOutcome);
    expect(await harness.readCommands(), isEmpty);
    expect(await testNote.readRecords(harness.database), {
      'server-note': 'from server',
      'local-note': 'local',
    });
    final [firstRequest, secondRequest] = harness.server.requests;
    final deviceId = (await harness.readState()).deviceId;
    expect(deviceId, isNotNull);
    expect(firstRequest.headers['Murmli-Device-Id'], deviceId);
    expect(secondRequest.headers['Murmli-Device-Id'], deviceId);
    expect(firstRequest.data, isNot(contains('token')));
    expect(secondRequest.data, {
      'token': firstToken,
      'dataTypes': ['testNote', 'testBoard', 'testCard'],
      'commands': [
        {
          'id': isA<String>(),
          'kind': 'create',
          'scopeId': personalScopeId,
          'dataType': 'testNote',
          'recordId': 'local-note',
          'time': harness.now.millisecondsSinceEpoch,
          'fields': {'text': 'local'},
        },
      ],
      'pendingCommandCount': 1,
      'oldestPendingCommandAgeSeconds': 0,
    });
  });

  test(
    'runRound removes confirmed commands and keeps every queued command '
    'visible, including one written while the request is in flight',
    () async {
      await harness.completeFirstRound();
      await harness.create(testNote, 'note-a', 'a');
      await harness.create(testNote, 'note-b', 'b');
      final [createNoteA, createNoteB] = await harness.readCommands();
      final held = harness.server.answerAfterRelease(
        (_) => harness.response(
          token: 'second-token',
          results: [commandResult(createNoteA.id, 'applied')],
          scopes: [
            scopeBlock(
              personalScopeId,
              fullState: true,
              records: [serverRecord(testNote, 'note-a', 'a')],
            ),
          ],
        ),
      );

      final round = harness.core.runRound();
      await held.arrived;
      await harness.setField(testNote, 'note-a', 'edited in flight');
      held.release();

      expect(await round, roundOutcomeRunningAgain);
      final [queuedCreate, queuedSetFields] = await harness.readCommands();
      expect(queuedCreate.id, createNoteB.id);
      expect(
        (queuedSetFields.kind, queuedSetFields.recordId),
        ('setFields', 'note-a'),
      );
      expect(await testNote.readRecords(harness.database), {
        'note-a': 'edited in flight',
        'note-b': 'b',
      });
    },
  );

  group('runRound with an unreadable response', () {
    Map<String, Object?> readableResponse(String commandId) => harness.response(
      token: 'second-token',
      results: [commandResult(commandId, 'applied')],
      scopes: [
        scopeBlock(
          personalScopeId,
          fullState: true,
          records: [serverRecord(testNote, 'server-note', 'changed')],
        ),
      ],
    );

    final unreadableBodies = <String, String Function(String commandId)>{
      'a missing field': (commandId) =>
          jsonEncode(readableResponse(commandId)..remove('token')),
      'a field of the wrong type': (commandId) =>
          jsonEncode(readableResponse(commandId)..['hasMore'] = 'yes'),
      'a token that is not text': (commandId) =>
          jsonEncode(readableResponse(commandId)..['token'] = 42),
      'an unknown result status': (commandId) => jsonEncode(
        readableResponse(commandId)
          ..['results'] = [commandResult(commandId, 'brandNew')],
      ),
      'a result for another command': (_) =>
          jsonEncode(readableResponse('another-command')),
      'a truncated body': (commandId) {
        final body = jsonEncode(readableResponse(commandId));
        return body.substring(0, body.length ~/ 2);
      },
      'a body that is not JSON': (_) => '<html>Bad gateway</html>',
    };

    for (final MapEntry(key: name, value: body) in unreadableBodies.entries) {
      test(
        'with $name changes neither data, queue nor token and fails',
        () async {
          await harness.completeFirstRound(
            records: [serverRecord(testNote, 'server-note', 'original')],
          );
          await harness.create(testNote, 'local-note', 'local');
          final [command] = await harness.readCommands();
          final before = await harness.snapshot();
          harness.server.answerRaw(body(command.id));

          final outcome = await harness.core.runRound();

          expect(outcome, const SyncRoundOutcome.failed());
          expect(await harness.snapshot(), before);
        },
      );
    }
  });

  test('runRound skips and reports a single unreadable record and applies the '
      'rest of the response', () async {
    await harness.completeFirstRound();
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        scopes: [
          scopeBlock(
            personalScopeId,
            records: [
              serverRecord(testNote, 'readable-note', 'readable'),
              {
                'dataType': 'testNote',
                'id': 'unreadable-note',
                'fields': {'text': 42},
              },
            ],
          ),
        ],
      ),
    );

    final outcome = await harness.core.runRound();

    expect(outcome, roundOutcome);
    expect(await testNote.readRecords(harness.database), {
      'readable-note': 'readable',
    });
    expect((await harness.readState()).token, 'second-token');
    expect(harness.crashReporter.calls, [
      'problem Skipped a sync record of testNote',
    ]);
  });

  test('runRound skips a record whose data type fails after writing part of '
      'it and applies the rest of the response', () async {
    final failingNote = FailingTestDataType();
    harness = await SyncTestHarness.start(
      dataTypes: [...testDataTypes, failingNote],
    );
    await harness.completeFirstRound();
    failingNote.writeServerRecordError = Exception('write failed');
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        scopes: [
          scopeBlock(
            personalScopeId,
            records: [
              serverRecord(failingNote, 'failing-note', 'partial'),
              serverRecord(testNote, 'readable-note', 'readable'),
            ],
          ),
        ],
      ),
    );

    final outcome = await harness.core.runRound();

    expect(outcome, roundOutcome);
    expect(await failingNote.readRecords(harness.database), isEmpty);
    expect(await testNote.readRecords(harness.database), {
      'readable-note': 'readable',
    });
    expect((await harness.readState()).token, 'second-token');
    expect(harness.crashReporter.calls, [
      'problem Skipped a sync record of failingNote',
    ]);
  });

  group('runRound answered with a failing status', () {
    final failingAnswers = <String, void Function(FakeSyncServer server)>{
      '400': (server) => server.answerProblem(400),
      '401 again after renewing the access token': (server) => server
        ..answerProblem(401)
        ..answerProblem(401),
      '403': (server) => server.answerProblem(403),
      '403 app-version-unsupported': (server) =>
          server.answerProblem(403, code: 'app-version-unsupported'),
      '404': (server) => server.answerProblem(404),
      '410 without account-gone': (server) =>
          server.answerProblem(410, code: 'gone'),
      '413 without request-too-large': (server) => server.answerProblem(413),
      '415': (server) => server.answerProblem(415),
      '429 rate-limited': (server) => server.answerProblem(
        429,
        code: 'rate-limited',
        headers: {
          'retry-after': ['7'],
        },
      ),
      '500': (server) => server.answerProblem(500),
      '502 with an HTML body': (server) =>
          server.answerRaw('<html>Bad gateway</html>', status: 502),
      '503 service-unavailable': (server) =>
          server.answerProblem(503, code: 'service-unavailable'),
      'a network error': (server) =>
          server.failWith(const SocketException('offline')),
    };

    for (final MapEntry(key: name, value: answer) in failingAnswers.entries) {
      test('$name keeps every queued command', () async {
        await harness.completeFirstRound();
        await harness.create(testNote, 'note-a', 'a');
        await harness.create(testNote, 'note-b', 'b');
        final before = await harness.readCommands();
        answer(harness.server);

        await harness.core.runRound();

        expect(await harness.readCommands(), before);
        expect(await testNote.readRecords(harness.database), {
          'note-a': 'a',
          'note-b': 'b',
        });
      });
    }
  });

  test('runRound answered with 401 again after renewing the access token '
      'passes the error to the interceptors, keeps every queued command and '
      'shows no error dialog', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    final before = await harness.readCommands();
    final interceptedStatuses = <int?>[];
    harness.container
        .read(apiClientProvider)
        .interceptors
        .add(
          InterceptorsWrapper(
            onError: (error, handler) {
              interceptedStatuses.add(error.response?.statusCode);
              handler.next(error);
            },
          ),
        );
    harness.server
      ..answerProblem(401)
      ..answerProblem(401);

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.failed());
    expect(interceptedStatuses, [401]);
    expect(await harness.readCommands(), before);
    expect(harness.container.read(failedApiRequestTraceIdProvider), isNull);
  });

  test('runRound answered with 429 and Retry-After waits as long as the server '
      'asks', () async {
    await harness.completeFirstRound();
    harness.server.answerProblem(
      429,
      code: 'rate-limited',
      headers: {
        'retry-after': ['7'],
      },
    );

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.retryAfter(Duration(seconds: 7)));
  });

  test('runRound answered with request-too-large sends smaller portions next '
      'and loses no command', () async {
    await harness.completeFirstRound();
    for (var index = 1; index <= 4; index++) {
      await harness.create(testNote, 'note-$index', 'text $index');
    }
    final queuedIds = await queuedCommandIds();
    harness.server.answerProblem(413, code: 'request-too-large');
    harness.answerApplyingAll(token: 'second-token');
    harness.answerApplyingAll(token: 'third-token');

    final outcomes = [
      await harness.core.runRound(),
      await harness.core.runRound(),
      await harness.core.runRound(),
    ];

    expect(outcomes, [
      roundOutcomeRunningAgain,
      roundOutcomeRunningAgain,
      roundOutcome,
    ]);
    final [_, tooLarge, firstPortion, secondPortion] =
        harness.server.requestBodies;
    expect(sentCommandIds(tooLarge), queuedIds);
    expect(sentCommandIds(firstPortion), hasLength(2));
    expect([
      ...sentCommandIds(firstPortion),
      ...sentCommandIds(secondPortion),
    ], queuedIds);
    expect(await harness.readCommands(), isEmpty);
  });

  test('runRound answered with request-too-large for a single command parks it '
      'and discards the token', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');

    harness.server.answerProblem(413, code: 'request-too-large');
    final outcome = await harness.core.runRound();

    expect(outcome, roundOutcomeRunningAgain);
    expect(await harness.readCommands(), isEmpty);
    expect((await harness.readState()).token, isNull);
    final [notice] = await harness.core.watchNotices().first;
    expect(
      (notice.kind, notice.recordId),
      (SyncNoticeKind.parkedCommand, 'note-a'),
    );
    expect(harness.crashReporter.calls, [
      'problem Parked a sync command: testNote create (too large)',
    ]);
  });

  test('runRound answered with request-too-large with implausible limits '
      'keeps the stored limits', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    await harness.create(testNote, 'note-b', 'b');
    harness.server.answerProblem(
      413,
      code: 'request-too-large',
      fields: {'maxRequestBytes': 10, 'maxCommandsPerRequest': 0},
    );

    await harness.core.runRound();

    final state = await harness.readState();
    expect(
      (state.maxCommandsPerRequest, state.maxRequestBytes),
      (500, 1000000),
    );
  });

  test(
    'runRound ignores implausible settings and keeps sending commands',
    () async {
      await harness.completeFirstRound();
      harness.server.answer(
        (_) => harness.response(token: 'second-token')
          ..['settings'] = {
            'pollIntervalSeconds': 0,
            'maxCommandsPerRequest': 0,
            'maxRequestBytes': 65535,
          },
      );

      await harness.core.runRound();
      final state = await harness.readState();
      await harness.create(testNote, 'note-a', 'a');
      harness.answerApplyingAll(token: 'third-token');
      final outcome = await harness.core.runRound();

      expect(
        (
          state.token,
          state.pollIntervalSeconds,
          state.maxCommandsPerRequest,
          state.maxRequestBytes,
        ),
        ('second-token', 60, 500, 1000000),
      );
      expect(outcome, roundOutcome);
      expect(sentCommandIds(harness.server.requestBodies.last), hasLength(1));
      expect(await harness.readCommands(), isEmpty);
    },
  );

  group('runRound with incomplete settings', () {
    final incompleteSettings =
        <String, void Function(Map<String, Object?> response)>{
          'no settings': (response) => response.remove('settings'),
          'no maxRequestBytes': (response) =>
              (response['settings']! as Map).remove('maxRequestBytes'),
        };

    for (final MapEntry(key: name, value: removeSettings)
        in incompleteSettings.entries) {
      test('with $name applies the response and keeps the stored '
          'settings', () async {
        await harness.completeFirstRound(maxRequestBytes: 200000);
        harness.server.answer((_) {
          final response = harness.response(token: 'second-token');
          removeSettings(response);
          return response;
        });

        final outcome = await harness.core.runRound();

        final state = await harness.readState();
        expect(outcome, roundOutcome);
        expect(
          (
            state.token,
            state.pollIntervalSeconds,
            state.maxCommandsPerRequest,
            state.maxRequestBytes,
          ),
          ('second-token', 60, 500, 200000),
        );
      });
    }
  });

  test('runRound splits queued commands larger than maxRequestBytes across '
      'requests before sending and loses none', () async {
    const maxRequestBytes = 65536;
    await harness.completeFirstRound(maxRequestBytes: maxRequestBytes);
    for (var index = 1; index <= 5; index++) {
      await harness.create(testCard, 'card-$index', '$index' * 20000);
    }
    final queuedIds = await queuedCommandIds();
    for (var index = 0; index < queuedIds.length; index++) {
      harness.answerApplyingAll(
        token: 'token-$index',
        maxRequestBytes: maxRequestBytes,
      );
    }

    SyncRoundOutcome outcome;
    do {
      outcome = await harness.core.runRound();
    } while (outcome == roundOutcomeRunningAgain);

    expect(outcome, roundOutcome);
    final commandRequests = harness.server.requestBodies.skip(1).toList();
    expect(commandRequests.length, greaterThan(1));
    expect([
      for (final requestBody in commandRequests)
        utf8.encode(jsonEncode(requestBody)).length,
    ], everyElement(lessThanOrEqualTo(maxRequestBytes)));
    expect([
      for (final requestBody in commandRequests) ...sentCommandIds(requestBody),
    ], queuedIds);
    expect(await harness.readCommands(), isEmpty);
  });

  test('runRound leaves a notice with the reason of a rejected command and '
      'answers an unknown reason as invalid', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    await harness.create(testNote, 'note-b', 'b');
    final [createNoteA, createNoteB] = await harness.readCommands();
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        results: [
          commandResult(createNoteA.id, 'rejected', reason: 'limitExceeded'),
          commandResult(createNoteB.id, 'rejected', reason: 'brandNew'),
        ],
      ),
    );

    await harness.core.runRound();

    expect(await harness.readCommands(), isEmpty);
    expect(
      [
        for (final notice in await harness.core.watchNotices().first)
          (notice.kind, notice.recordId, notice.reason),
      ],
      [
        (SyncNoticeKind.rejectedCommand, 'note-a', 'limitExceeded'),
        (SyncNoticeKind.rejectedCommand, 'note-b', 'invalid'),
      ],
    );
  });

  test('runRound parks a command failed five times over an hour, discards the '
      'token at once and restores the server state next round', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    final [command] = await harness.readCommands();
    final start = harness.now;
    Future<SyncRoundOutcome> failAttempt(int attempt) {
      harness
        ..now = start.add(Duration(minutes: 15 * (attempt - 1)))
        ..server.answer(
          (_) => harness.response(
            token: 'token-$attempt',
            results: [commandResult(command.id, 'failed')],
          ),
        );
      return harness.core.runRound();
    }

    final outcomes = [
      for (var attempt = 1; attempt <= 4; attempt++) await failAttempt(attempt),
    ];
    final [failingCommand] = await harness.readCommands();
    outcomes.add(await failAttempt(5));

    expect(failingCommand.failedAttempts, 4);
    expect(outcomes, everyElement(roundOutcome));
    expect(await harness.readCommands(), isEmpty);
    expect((await harness.readState()).token, isNull);
    final [notice] = await harness.core.watchNotices().first;
    expect(
      (notice.kind, notice.recordId),
      (SyncNoticeKind.parkedCommand, 'note-a'),
    );
    expect(harness.crashReporter.calls, [
      'problem Parked a sync command: testNote create (failed repeatedly)',
    ]);

    harness.server.answer(
      (_) => harness.response(
        token: 'full-state-token',
        coversAllScopes: true,
        scopes: [scopeBlock(personalScopeId, fullState: true)],
      ),
    );
    await harness.core.runRound();

    expect(harness.server.requestBodies.last, isNot(contains('token')));
    expect(await testNote.readRecords(harness.database), isEmpty);
  });

  test('runRound does not ask to run again immediately once only a failing '
      'command is left', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    await harness.create(testNote, 'note-b', 'b');
    final [createNoteA, createNoteB] = await harness.readCommands();
    harness.server
      ..answer(
        (_) => harness.response(
          token: 'second-token',
          results: [
            commandResult(createNoteA.id, 'applied'),
            commandResult(createNoteB.id, 'failed'),
          ],
        ),
      )
      ..answer(
        (_) => harness.response(
          token: 'third-token',
          results: [commandResult(createNoteB.id, 'failed')],
        ),
      )
      ..answer(
        (_) => harness.response(
          token: 'fourth-token',
          results: [commandResult(createNoteB.id, 'failed')],
        ),
      );

    final outcomes = [
      await harness.core.runRound(),
      await harness.core.runRound(),
      await harness.core.runRound(),
    ];

    expect(outcomes, [roundOutcomeRunningAgain, roundOutcome, roundOutcome]);
  });

  test('runRound keeps an accepted command queued without a failed attempt and '
      'sends it again next round', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    final [command] = await harness.readCommands();
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        results: [commandResult(command.id, 'accepted')],
      ),
    );
    harness.answerApplyingAll(token: 'third-token');

    final outcome = await harness.core.runRound();
    final [acceptedCommand] = await harness.readCommands();
    await harness.core.runRound();

    expect(outcome, roundOutcome);
    expect(
      (acceptedCommand.id, acceptedCommand.failedAttempts),
      (command.id, 0),
    );
    expect(sentCommandIds(harness.server.requestBodies.last), [command.id]);
  });

  test('runRound called during a running round shares exactly one follow-up '
      'round and never overlaps', () async {
    await harness.completeFirstRound();
    final held = harness.server.answerAfterRelease(
      (_) => harness.response(token: 'second-token'),
    );
    harness.server.answer((_) => harness.response(token: 'third-token'));

    final firstRound = harness.core.runRound();
    await held.arrived;
    final followUpRounds = [
      harness.core.runRound(),
      harness.core.runRound(),
      harness.core.runRound(),
    ];
    await pumpEventQueue();
    final requestsWhileHeld = harness.server.requests.length;
    held.release();
    await Future.wait([firstRound, ...followUpRounds]);

    expect(requestsWhileHeld, 2);
    expect(followUpRounds.toSet(), hasLength(1));
    expect(
      [
        for (final requestBody in harness.server.requestBodies)
          requestBody['token'],
      ],
      [null, firstToken, 'second-token'],
    );
  });

  group('runRound with a rejected command', () {
    setUp(() async {
      await harness.completeFirstRound();
      await harness.create(testNote, 'note-a', 'a');
      final [command] = await harness.readCommands();
      harness.server.answer(
        (_) => harness.response(
          token: 'second-token',
          results: [commandResult(command.id, 'rejected', reason: 'invalid')],
        ),
      );
      await harness.core.runRound();
    });

    Future<List<String?>> noticeRecordIds() async => [
      for (final notice in await harness.core.watchNotices().first)
        notice.recordId,
    ];

    test('keeps its notice for 30 days and removes it with the next applied '
        'response after that', () async {
      harness
        ..now = harness.now.add(const Duration(days: 29))
        ..server.answer((_) => harness.response(token: 'third-token'));
      await harness.core.runRound();
      final noticesAfter29Days = await noticeRecordIds();
      harness
        ..now = harness.now.add(const Duration(days: 2))
        ..server.answer((_) => harness.response(token: 'fourth-token'));

      await harness.core.runRound();

      expect(noticesAfter29Days, ['note-a']);
      expect(await noticeRecordIds(), isEmpty);
    });

    test('dismissNotice removes its notice', () async {
      final [notice] = await harness.core.watchNotices().first;

      await harness.core.dismissNotice(notice.id);

      expect(await noticeRecordIds(), isEmpty);
    });
  });

  test('runRound with alreadyProcessed results removes a command originally '
      'applied and leaves a notice for one originally rejected', () async {
    await harness.completeFirstRound();
    await harness.create(testNote, 'note-a', 'a');
    await harness.create(testNote, 'note-b', 'b');
    final [createNoteA, createNoteB] = await harness.readCommands();
    harness.server.answer(
      (_) => harness.response(
        token: 'second-token',
        results: [
          {
            'commandId': createNoteA.id,
            'status': 'alreadyProcessed',
            'original': {'status': 'applied'},
          },
          {
            'commandId': createNoteB.id,
            'status': 'alreadyProcessed',
            'original': {'status': 'rejected', 'reason': 'noAccess'},
          },
        ],
      ),
    );

    await harness.core.runRound();

    expect(await harness.readCommands(), isEmpty);
    expect(
      [
        for (final notice in await harness.core.watchNotices().first)
          (notice.kind, notice.recordId, notice.reason),
      ],
      [(SyncNoticeKind.rejectedCommand, 'note-b', 'noAccess')],
    );
  });

  test('runRound discards the token and reports it after the third failed '
      'application in a row', () async {
    await harness.completeFirstRound();
    for (var attempt = 1; attempt <= 3; attempt++) {
      harness.server.answer(
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
    }
    await harness.core.runRound();
    await harness.core.runRound();
    final tokenAfterTwoFailures = (await harness.readState()).token;
    final reportsAfterTwoFailures = [...harness.crashReporter.calls];

    final outcome = await harness.core.runRound();

    expect(outcome, const SyncRoundOutcome.failed());
    expect(tokenAfterTwoFailures, firstToken);
    expect(reportsAfterTwoFailures, isEmpty);
    expect((await harness.readState()).token, isNull);
    expect(harness.crashReporter.calls, [
      'problem Applying a sync response failed repeatedly',
    ]);
  });

  test('runRound parks a single command larger than maxRequestBytes before '
      'sending it and discards the token', () async {
    const maxRequestBytes = 65536;
    await harness.completeFirstRound(maxRequestBytes: maxRequestBytes);
    await harness.create(testCard, 'large-card', 'x' * maxRequestBytes);
    final [command] = await harness.readCommands();
    harness.answerApplyingAll(
      token: 'second-token',
      maxRequestBytes: maxRequestBytes,
    );

    final outcome = await harness.core.runRound();

    expect(outcome, roundOutcome);
    expect([
      for (final requestBody in harness.server.requestBodies)
        ...sentCommandIds(requestBody),
    ], isNot(contains(command.id)));
    expect(harness.server.requestBodies.last['token'], isNull);
    expect(await harness.readCommands(), isEmpty);
    expect(
      [
        for (final notice in await harness.core.watchNotices().first)
          (notice.kind, notice.recordId),
      ],
      [(SyncNoticeKind.parkedCommand, 'large-card')],
    );
    expect(harness.crashReporter.calls, [
      'problem Parked a sync command: testCard create (too large)',
    ]);
  });
}
