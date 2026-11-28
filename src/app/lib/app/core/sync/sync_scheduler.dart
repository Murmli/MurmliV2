import 'dart:async';
import 'dart:math';

import 'package:clock/clock.dart';
import 'package:flutter/foundation.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../authentication/signed_in_account_provider.dart';
import '../lifecycle/app_foreground_provider.dart';
import '../network/network_available_provider.dart';
import '../time/clock_provider.dart';
import 'sync_activity.dart';
import 'sync_core.dart';
import 'sync_random_provider.dart';
import 'sync_round_outcome.dart';

part 'sync_scheduler.g.dart';

const _defaultPollInterval = Duration(seconds: 60);
const _writeDelay = Duration(milliseconds: 300);
const _backoffSteps = [
  Duration(seconds: 1),
  Duration(seconds: 2),
  Duration(seconds: 5),
  Duration(seconds: 10),
  Duration(seconds: 30),
  Duration(seconds: 60),
];

@Riverpod(keepAlive: true)
SyncScheduler syncScheduler(Ref ref) {
  final scheduler = SyncScheduler(
    core: ref.watch(syncCoreProvider),
    clock: ref.watch(appClockProvider),
    random: ref.watch(syncRandomProvider),
  );
  ref
    ..onDispose(scheduler._dispose)
    ..listen(appForegroundProvider, (_, inForeground) {
      if (inForeground) {
        scheduler._enterForeground();
      } else {
        scheduler._enterBackground();
      }
    }, fireImmediately: true)
    ..listen(
      networkAvailableProvider,
      (_, available) => scheduler._changeNetworkAvailability(available),
      fireImmediately: true,
    )
    ..listen(signedInAccountProvider, (previous, next) {
      if (next != null) {
        // The listener of syncCoreProvider may run after this one, and a
        // round only waits for an account change that has already begun.
        scheduleMicrotask(scheduler._signIn);
      } else if (previous != null) {
        scheduler._signOut();
      }
    }, fireImmediately: true);
  return scheduler;
}

enum _Wait { write, poll, backoff, server }

enum _FollowUp { none, write, forced }

/// The only caller of [SyncCore.runRound]: runs rounds after sign-in, on
/// returning to the foreground, after writes, when the network returns, on
/// polls, after failures and on [syncNow]. No timer runs in the background.
/// One round sequence runs at a time and at most one round timer is pending.
class SyncScheduler {
  SyncScheduler({
    required this._core,
    required this._clock,
    required this._random,
  }) {
    _commandsQueuedSubscription = _core.commandsQueued.listen(
      (_) => _onCommandsQueued(),
    );
  }

  final SyncCore _core;
  final Clock _clock;
  final Random _random;
  late final StreamSubscription<void> _commandsQueuedSubscription;
  final _activity = ValueNotifier(SyncActivity.stopped);
  Timer? _timer;
  _Wait? _wait;
  var _halted = true;
  var _running = false;
  var _followUp = _FollowUp.none;
  var _disposed = false;
  var _inForeground = true;
  var _networkAvailable = true;
  var _consecutiveFailures = 0;
  DateTime? _serverWaitUntil;
  Completer<void>? _nextRoundDone;

  ValueListenable<SyncActivity> get activity => _activity;

  /// Runs a round now, also during the backoff after failures, and completes
  /// when it has finished. Completes at once without a round while the
  /// server asked to wait.
  Future<void> syncNow() {
    if (_disposed || _serverWaitRemaining() != null) {
      return Future.value();
    }
    final done = (_nextRoundDone ??= Completer()).future;
    _runOrFollowUp(_FollowUp.forced);
    return done;
  }

  void _signIn() {
    if (_disposed) {
      return;
    }
    _halted = false;
    _consecutiveFailures = 0;
    _serverWaitUntil = null;
    _runOrFollowUp(_FollowUp.forced);
  }

  void _signOut() {
    _halted = true;
    _cancelTimer();
    _serverWaitUntil = null;
    _publishActivity();
  }

  void _enterForeground() {
    if (_inForeground) {
      return;
    }
    _inForeground = true;
    if (!_disposed && !_halted) {
      if (_serverWaitRemaining() case final serverWait?) {
        _schedule(serverWait, _Wait.server);
      } else {
        _runOrFollowUp(_FollowUp.forced);
      }
    }
    _publishActivity();
  }

  void _enterBackground() {
    if (!_inForeground) {
      return;
    }
    _inForeground = false;
    _cancelTimer();
    if (!_running) {
      unawaited(_runLastRoundIfCommandsWait());
    }
    _publishActivity();
  }

  Future<void> _runLastRoundIfCommandsWait() async {
    final commandsWait = await _core.pendingCommandCount().then(
      (count) => count > 0,
      onError: (Object _) => false,
    );
    if (commandsWait &&
        !_disposed &&
        !_inForeground &&
        !_running &&
        !_halted &&
        _serverWaitRemaining() == null) {
      _runOrFollowUp(_FollowUp.forced);
    }
  }

  void _changeNetworkAvailability(bool available) {
    final returned = available && !_networkAvailable;
    _networkAvailable = available;
    if (returned &&
        !_disposed &&
        !_halted &&
        _inForeground &&
        _serverWaitRemaining() == null) {
      _runOrFollowUp(_FollowUp.forced);
    }
    _publishActivity();
  }

  Future<void> _dispose() async {
    _disposed = true;
    _cancelTimer();
    await _commandsQueuedSubscription.cancel();
    _activity.dispose();
  }

  // A write during a backoff or a wait asked for by the server waits for it,
  // so writes do not hammer a failing server.
  void _onCommandsQueued() {
    if (_halted || _wait == _Wait.backoff || _serverWaitRemaining() != null) {
      return;
    }
    if (_running) {
      _runOrFollowUp(_FollowUp.write);
    } else {
      _schedule(_writeDelay, _Wait.write);
    }
  }

  void _runOrFollowUp(_FollowUp followUp) {
    if (!_running) {
      _cancelTimer();
      _running = true;
      unawaited(_runRounds());
    } else if (followUp.index > _followUp.index) {
      _followUp = followUp;
    }
    _publishActivity();
  }

  Future<void> _runRounds() async {
    try {
      while (await _runRound()) {}
    } finally {
      _running = false;
      _nextRoundDone?.complete();
      _nextRoundDone = null;
      _publishActivity();
    }
  }

  /// Returns whether the next round runs at once. Nothing after the last
  /// await yields, so no trigger slips in between this decision and the
  /// reset of `_running`.
  Future<bool> _runRound() async {
    _followUp = _FollowUp.none;
    final done = _nextRoundDone;
    _nextRoundDone = null;
    final outcome = await _core.runRound();
    done?.complete();
    if (_disposed) {
      return false;
    }
    final pollInterval =
        outcome == const SyncRoundOutcome.completed(runAgainImmediately: false)
        ? await _core.pollInterval() ?? _defaultPollInterval
        : null;
    if (_disposed) {
      return false;
    }
    switch (outcome) {
      case SyncRoundRetryAfter(:final delay):
        if (!_halted) {
          _serverWaitUntil = _clock.now().add(delay);
          _schedule(delay, _Wait.server);
        }
        return false;
      case SyncRoundCompleted():
        _serverWaitUntil = null;
        _consecutiveFailures = 0;
      case SyncRoundFailed():
        _serverWaitUntil = null;
      case SyncRoundNotSignedIn() ||
          SyncRoundAccountGone() ||
          SyncRoundAppVersionUnsupported():
        _serverWaitUntil = null;
        if (_followUp != _FollowUp.forced) {
          _halted = true;
        }
    }
    if (_followUp == _FollowUp.forced) {
      return true;
    }
    if (_halted) {
      return false;
    }
    switch (outcome) {
      case SyncRoundCompleted(runAgainImmediately: true):
        return true;
      case SyncRoundCompleted() when _followUp == _FollowUp.write:
        return true;
      case SyncRoundCompleted():
        _schedule(pollInterval!, _Wait.poll);
      case SyncRoundFailed():
        _schedule(_nextBackoffDelay(), _Wait.backoff);
      case SyncRoundRetryAfter() ||
          SyncRoundNotSignedIn() ||
          SyncRoundAccountGone() ||
          SyncRoundAppVersionUnsupported():
        break;
    }
    return false;
  }

  // A random part of the step, so devices do not retry in lockstep after an
  // outage.
  Duration _nextBackoffDelay() =>
      _backoffSteps[min(_consecutiveFailures++, _backoffSteps.length - 1)] *
      _random.nextDouble();

  // In the background no timer starts; a wait asked for by the server lives
  // on in _serverWaitUntil.
  void _schedule(Duration delay, _Wait wait) {
    _cancelTimer();
    if (_inForeground) {
      _wait = wait;
      _timer = Timer(delay, () {
        _timer = null;
        _wait = null;
        if (wait == _Wait.server) {
          _serverWaitUntil = null;
        }
        _runOrFollowUp(_FollowUp.forced);
      });
    }
    _publishActivity();
  }

  void _cancelTimer() {
    _timer?.cancel();
    _timer = null;
    _wait = null;
  }

  Duration? _serverWaitRemaining() {
    final until = _serverWaitUntil;
    if (until == null) {
      return null;
    }
    final remaining = until.difference(_clock.now());
    if (remaining <= Duration.zero) {
      _serverWaitUntil = null;
      return null;
    }
    return remaining;
  }

  void _publishActivity() {
    if (!_disposed) {
      _activity.value = _currentActivity();
    }
  }

  SyncActivity _currentActivity() {
    if (_running) {
      return SyncActivity.syncing;
    }
    if (_halted) {
      return SyncActivity.stopped;
    }
    if (_wait == _Wait.server || _serverWaitRemaining() != null) {
      return SyncActivity.waitingForServer;
    }
    if (!_inForeground) {
      return SyncActivity.paused;
    }
    if (!_networkAvailable) {
      return SyncActivity.waitingForNetwork;
    }
    if (_wait == _Wait.backoff) {
      return SyncActivity.backingOff;
    }
    return SyncActivity.idle;
  }
}
