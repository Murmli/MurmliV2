import 'dart:async';

import 'package:dio/dio.dart';
import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/lifecycle/app_foreground_provider.dart';
import 'package:app/app/core/network/network_available_provider.dart';
import 'package:app/app/core/sync/sync_activity.dart';
import 'package:app/app/core/sync/sync_scheduler.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/http_client_adapter.dart';
import 'sync_test_setup.dart';
import 'test_data_types.dart';

const _writeDelay = Duration(milliseconds: 300);

void main() {
  void schedulerTest(
    String description,
    void Function(FakeAsync async, SyncTestHarness harness) body,
  ) => test(description, () {
    fakeAsync(
      (async) => body(
        async,
        _settle(
          async,
          SyncTestHarness.start(
            clock: async.getClock(DateTime.utc(2026, 11, 26, 12)),
          ),
        ),
      ),
    );
  });

  SyncScheduler startScheduler(
    FakeAsync async,
    SyncTestHarness harness, {
    int pollIntervalSeconds = 60,
  }) {
    harness.answerFirstRound(pollIntervalSeconds: pollIntervalSeconds);
    final scheduler = harness.container.read(syncSchedulerProvider);
    async.elapse(Duration.zero);
    expect(harness.server.requests, hasLength(1));
    return scheduler;
  }

  void setSignedInAccount(SyncTestHarness harness, Account? account) =>
      harness.container.read(signedInAccountProvider.notifier).state = account;

  void setForeground(SyncTestHarness harness, bool inForeground) =>
      harness.container.read(appForegroundProvider.notifier).state =
          inForeground;

  void setNetworkAvailable(SyncTestHarness harness, bool available) =>
      harness.container.read(networkAvailableProvider.notifier).state =
          available;

  void writeNote(FakeAsync async, SyncTestHarness harness, String recordId) {
    _settle(async, harness.create(testNote, recordId, recordId));
    async.elapse(Duration.zero);
  }

  void writeNoteAndWait(
    FakeAsync async,
    SyncTestHarness harness,
    String recordId,
  ) {
    writeNote(async, harness, recordId);
    async.elapse(_writeDelay);
  }

  void expectRequestsAfter(
    FakeAsync async,
    SyncTestHarness harness,
    Duration delay,
  ) {
    final requestCount = harness.server.requests.length;
    async.elapse(delay - const Duration(milliseconds: 1));
    expect(harness.server.requests, hasLength(requestCount));
    async.elapse(const Duration(milliseconds: 1));
    expect(harness.server.requests, hasLength(requestCount + 1));
  }

  void failNextRoundWithServerWait(SyncTestHarness harness, int seconds) =>
      harness.server.answerProblem(
        429,
        code: 'rate-limited',
        headers: {
          'retry-after': ['$seconds'],
        },
      );

  schedulerTest('starting signed in runs the first round', (async, harness) {
    startScheduler(async, harness);

    expect(_settle(async, harness.readState()).firstSyncCompleted, isTrue);
  });

  schedulerTest('signing in after signing out runs a round', (async, harness) {
    startScheduler(async, harness);
    setSignedInAccount(harness, null);
    async.elapse(Duration.zero);

    harness.answerFirstRound();
    setSignedInAccount(harness, const Account(id: accountId));
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(2));
    expect(_settle(async, harness.readState()).firstSyncCompleted, isTrue);
  });

  schedulerTest('the first round after signing in waits for the account '
      'request', (async, harness) {
    startScheduler(async, harness);
    setSignedInAccount(harness, null);
    async.elapse(Duration.zero);
    final accountResponse = Completer<ResponseBody>();
    harness.container
        .read(apiClientProvider)
        .httpClientAdapter = RecordingHttpClientAdapter(
      respond: (request) => request.path == '/v1/users/me'
          ? accountResponse.future
          : harness.server.adapter.fetch(request, null, null),
    );
    harness.answerFirstRound();

    unawaited(
      harness.container
          .read(signedInAccountProvider.notifier)
          .signInWithEmail(email: signInEmail, password: signInPassword),
    );
    async.elapse(Duration.zero);
    expect(harness.server.requests, hasLength(1));

    accountResponse.complete(jsonResponse({'id': accountId}));
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(2));
    expect(_settle(async, harness.readState()).firstSyncCompleted, isTrue);
  });

  schedulerTest('a write runs a round 300 ms later instead of waiting for the '
      'poll', (async, harness) {
    startScheduler(async, harness);
    harness.answerApplyingAll(token: 'second-token');

    writeNote(async, harness, 'note-a');

    expectRequestsAfter(async, harness, _writeDelay);
    expect(_settle(async, harness.readCommands()), isEmpty);
  });

  schedulerTest('writes in quick succession go out in one request 300 ms '
      'after the last one', (async, harness) {
    startScheduler(async, harness);
    harness.answerApplyingAll(token: 'second-token');

    writeNote(async, harness, 'note-a');
    async.elapse(const Duration(milliseconds: 200));
    writeNote(async, harness, 'note-b');

    expectRequestsAfter(async, harness, _writeDelay);
    expect(sentCommandIds(harness.server.requestBodies.last), hasLength(2));
  });

  schedulerTest('triggers during a round run exactly one more round right '
      'after it', (async, harness) {
    final scheduler = startScheduler(async, harness);
    final heldPoll = harness.server.answerAfterRelease(
      (_) => harness.response(token: 'second-token'),
    );
    async.elapse(const Duration(seconds: 60));
    expect(harness.server.requests, hasLength(2));

    writeNoteAndWait(async, harness, 'note-a');
    unawaited(scheduler.syncNow());
    setNetworkAvailable(harness, false);
    async.elapse(Duration.zero);
    setNetworkAvailable(harness, true);
    async.elapse(Duration.zero);
    expect(harness.server.requests, hasLength(2));
    harness.answerApplyingAll(token: 'third-token');

    heldPoll.release();
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(3));
    expect(_settle(async, harness.readCommands()), isEmpty);
  });

  schedulerTest('syncNow during a round runs one more round and completes '
      'after it', (async, harness) {
    final heldFirstRound = harness.server.answerAfterRelease(
      (_) => harness.response(
        token: firstToken,
        coversAllScopes: true,
        scopes: [scopeBlock(personalScopeId, fullState: true)],
      ),
    );
    final scheduler = harness.container.read(syncSchedulerProvider);
    async.elapse(Duration.zero);
    var completed = false;
    unawaited(scheduler.syncNow().then((_) => completed = true));
    harness.server.answer((_) => harness.response(token: 'second-token'));

    heldFirstRound.release();
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(2));
    expect(completed, isTrue);
  });

  schedulerTest(
    'a round with more to fetch runs the next one without waiting',
    (async, harness) {
      harness.server
        ..answer(
          (_) => harness.response(
            token: firstToken,
            coversAllScopes: true,
            hasMore: true,
            scopes: [scopeBlock(personalScopeId, fullState: true)],
          ),
        )
        ..answer((_) => harness.response(token: 'second-token'));

      harness.container.read(syncSchedulerProvider);
      async.elapse(Duration.zero);

      expect(harness.server.requests, hasLength(2));
    },
  );

  schedulerTest('a command that is only accepted waits for the next poll', (
    async,
    harness,
  ) {
    startScheduler(async, harness);
    harness.server.answer(
      (requestBody) => harness.response(
        token: 'second-token',
        results: [
          for (final commandId in sentCommandIds(requestBody))
            commandResult(commandId, 'accepted'),
        ],
      ),
    );
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));
    harness.server.answer((_) => harness.response(token: 'third-token'));

    expectRequestsAfter(async, harness, const Duration(seconds: 60));
  });

  schedulerTest('the next poll runs after the interval of the server', (
    async,
    harness,
  ) {
    startScheduler(async, harness, pollIntervalSeconds: 90);
    harness.server.answer((_) => harness.response(token: 'second-token'));

    expectRequestsAfter(async, harness, const Duration(seconds: 90));
  });

  schedulerTest('a wait asked for by the server delays the next round by '
      'exactly that time, also for writes and syncNow', (async, harness) {
    final scheduler = startScheduler(async, harness);
    failNextRoundWithServerWait(harness, 30);
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));

    writeNoteAndWait(async, harness, 'note-b');
    unawaited(scheduler.syncNow());
    async.elapse(Duration.zero);
    harness.answerApplyingAll(token: 'second-token');

    expectRequestsAfter(
      async,
      harness,
      const Duration(seconds: 30) - _writeDelay,
    );
  });

  schedulerTest('failed rounds wait a random part of 1, 2, 5, 10, 30 and '
      'then 60 seconds and start over after a completed round', (
    async,
    harness,
  ) {
    startScheduler(async, harness);
    // Requests without a scripted response fail.
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));

    for (final milliseconds in [500, 1000, 2500, 5000, 15000, 30000, 30000]) {
      expectRequestsAfter(async, harness, Duration(milliseconds: milliseconds));
    }
    harness.answerApplyingAll(token: 'second-token');
    expectRequestsAfter(async, harness, const Duration(seconds: 30));
    expect(_settle(async, harness.readCommands()), isEmpty);

    writeNoteAndWait(async, harness, 'note-b');
    expect(harness.server.requests, hasLength(11));
    expectRequestsAfter(async, harness, const Duration(milliseconds: 500));
  });

  schedulerTest('a write during the backoff waits for it', (async, harness) {
    startScheduler(async, harness);
    writeNoteAndWait(async, harness, 'note-a');
    async.elapse(const Duration(milliseconds: 100));

    writeNote(async, harness, 'note-b');

    expectRequestsAfter(async, harness, const Duration(milliseconds: 400));
  });

  schedulerTest('syncNow during the backoff runs a round at once and '
      'completes after it', (async, harness) {
    final scheduler = startScheduler(async, harness);
    writeNoteAndWait(async, harness, 'note-a');
    harness.answerApplyingAll(token: 'second-token');

    var completed = false;
    unawaited(scheduler.syncNow().then((_) => completed = true));
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(3));
    expect(completed, isTrue);
    expect(_settle(async, harness.readCommands()), isEmpty);
  });

  schedulerTest('a round without a signed-in account stops all timers', (
    async,
    harness,
  ) {
    startScheduler(async, harness);
    _settle(async, harness.core.adoptAccount(null));
    async.elapse(const Duration(seconds: 60));
    _settle(async, harness.core.adoptAccount(accountId));

    async.elapse(const Duration(hours: 1));

    expect(harness.server.requests, hasLength(1));
  });

  schedulerTest('after an unsupported app version neither writes nor polls '
      'run rounds', (async, harness) {
    startScheduler(async, harness);
    harness.server.answerProblem(403, code: 'app-version-unsupported');
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));

    writeNoteAndWait(async, harness, 'note-b');
    async.elapse(const Duration(hours: 1));

    expect(harness.server.requests, hasLength(2));
  });

  schedulerTest('signing out cancels the poll', (async, harness) {
    startScheduler(async, harness);
    setSignedInAccount(harness, null);
    async.elapse(Duration.zero);
    _settle(async, harness.core.adoptAccount(accountId));

    async.elapse(const Duration(hours: 1));

    expect(harness.server.requests, hasLength(1));
  });

  schedulerTest('signing out cancels the running request and all timers', (
    async,
    harness,
  ) {
    startScheduler(async, harness);
    harness.server.answerAfterRelease((_) => harness.response(token: 'never'));
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));

    setSignedInAccount(harness, null);
    async.elapse(Duration.zero);

    expect(harness.server.requests.last.cancelToken!.isCancelled, isTrue);
    _settle(async, harness.core.adoptAccount(accountId));
    async.elapse(const Duration(hours: 1));
    expect(harness.server.requests, hasLength(2));
  });

  schedulerTest('a hanging request ends after 60 seconds without a report and '
      'does not block the next round', (async, harness) {
    startScheduler(async, harness);
    harness.server.answerAfterRelease((_) => harness.response(token: 'never'));
    harness.answerApplyingAll(token: 'second-token');

    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));

    expectRequestsAfter(async, harness, const Duration(milliseconds: 60500));
    expect(_settle(async, harness.readCommands()), isEmpty);
    expect(harness.crashReporter.calls, isEmpty);
  });

  schedulerTest('in the background no round runs, and returning to the '
      'foreground runs one at once', (async, harness) {
    startScheduler(async, harness);
    setForeground(harness, false);
    async
      ..elapse(Duration.zero)
      ..elapse(const Duration(hours: 1));
    expect(harness.server.requests, hasLength(1));

    harness.server.answer((_) => harness.response(token: 'second-token'));
    setForeground(harness, true);
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(2));
  });

  schedulerTest('going to the background with waiting commands runs one last '
      'round and then no more', (async, harness) {
    startScheduler(async, harness);
    writeNote(async, harness, 'note-a');
    harness.answerApplyingAll(token: 'second-token');

    setForeground(harness, false);
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(2));
    expect(_settle(async, harness.readCommands()), isEmpty);
    async.elapse(const Duration(hours: 1));
    expect(harness.server.requests, hasLength(2));
  });

  schedulerTest('going to the background without waiting commands runs no '
      'round', (async, harness) {
    startScheduler(async, harness);

    setForeground(harness, false);
    async
      ..elapse(Duration.zero)
      ..elapse(const Duration(hours: 1));

    expect(harness.server.requests, hasLength(1));
  });

  schedulerTest('a wait asked for by the server holds across the background', (
    async,
    harness,
  ) {
    startScheduler(async, harness);
    failNextRoundWithServerWait(harness, 30);
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));
    async.elapse(const Duration(seconds: 10));

    setForeground(harness, false);
    async
      ..elapse(Duration.zero)
      ..elapse(const Duration(seconds: 10));
    harness.answerApplyingAll(token: 'second-token');
    setForeground(harness, true);
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(2));
    expectRequestsAfter(async, harness, const Duration(seconds: 10));
  });

  schedulerTest('the network returning runs a round at once, also during the '
      'backoff', (async, harness) {
    startScheduler(async, harness);
    writeNoteAndWait(async, harness, 'note-a');
    expect(harness.server.requests, hasLength(2));
    setNetworkAvailable(harness, false);
    async.elapse(Duration.zero);
    expect(harness.server.requests, hasLength(2));
    harness.answerApplyingAll(token: 'second-token');

    setNetworkAvailable(harness, true);
    async.elapse(Duration.zero);

    expect(harness.server.requests, hasLength(3));
    expect(_settle(async, harness.readCommands()), isEmpty);
  });

  schedulerTest('without a reported network the poll still runs', (
    async,
    harness,
  ) {
    startScheduler(async, harness);
    setNetworkAvailable(harness, false);
    harness.server.answer((_) => harness.response(token: 'second-token'));

    expectRequestsAfter(async, harness, const Duration(seconds: 60));
  });

  schedulerTest('the activity reports syncing, idle, waiting for the network, '
      'backing off, paused and stopped', (async, harness) {
    final heldFirstRound = harness.server.answerAfterRelease(
      (_) => harness.response(
        token: firstToken,
        coversAllScopes: true,
        scopes: [scopeBlock(personalScopeId, fullState: true)],
      ),
    );
    final scheduler = harness.container.read(syncSchedulerProvider);
    async.elapse(Duration.zero);
    expect(scheduler.activity.value, SyncActivity.syncing);

    heldFirstRound.release();
    async.elapse(Duration.zero);
    expect(scheduler.activity.value, SyncActivity.idle);

    writeNoteAndWait(async, harness, 'note-a');
    expect(scheduler.activity.value, SyncActivity.backingOff);

    setNetworkAvailable(harness, false);
    async.elapse(Duration.zero);
    expect(scheduler.activity.value, SyncActivity.waitingForNetwork);

    setForeground(harness, false);
    async.elapse(Duration.zero);
    expect(scheduler.activity.value, SyncActivity.paused);

    setSignedInAccount(harness, null);
    async.elapse(Duration.zero);
    expect(scheduler.activity.value, SyncActivity.stopped);
  });
}

T _settle<T>(FakeAsync async, Future<T> future) {
  T? result;
  Object? error;
  var completed = false;
  unawaited(
    future.then(
      (value) {
        result = value;
        completed = true;
      },
      onError: (Object caught) {
        error = caught;
        completed = true;
      },
    ),
  );
  async.elapse(Duration.zero);
  if (!completed) {
    throw StateError('The future did not complete without elapsing time');
  }
  if (error case final caught?) {
    throw caught;
  }
  return result as T;
}
