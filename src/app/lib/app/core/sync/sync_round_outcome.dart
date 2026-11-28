import 'package:flutter/foundation.dart';

@immutable
sealed class SyncRoundOutcome {
  const SyncRoundOutcome();

  const factory SyncRoundOutcome.completed({
    required bool runAgainImmediately,
  }) = SyncRoundCompleted;

  const factory SyncRoundOutcome.notSignedIn() = SyncRoundNotSignedIn;

  const factory SyncRoundOutcome.retryAfter(Duration delay) =
      SyncRoundRetryAfter;

  const factory SyncRoundOutcome.failed() = SyncRoundFailed;

  const factory SyncRoundOutcome.accountGone() = SyncRoundAccountGone;

  const factory SyncRoundOutcome.appVersionUnsupported() =
      SyncRoundAppVersionUnsupported;

  @override
  bool operator ==(Object other) => other.runtimeType == runtimeType;

  @override
  int get hashCode => runtimeType.hashCode;
}

class SyncRoundCompleted extends SyncRoundOutcome {
  const SyncRoundCompleted({required this.runAgainImmediately});

  final bool runAgainImmediately;

  @override
  bool operator ==(Object other) =>
      other is SyncRoundCompleted &&
      other.runAgainImmediately == runAgainImmediately;

  @override
  int get hashCode => runAgainImmediately.hashCode;

  @override
  String toString() =>
      'SyncRoundCompleted(runAgainImmediately: $runAgainImmediately)';
}

class SyncRoundNotSignedIn extends SyncRoundOutcome {
  const SyncRoundNotSignedIn();
}

/// The server asked to wait this long (429 or 503).
class SyncRoundRetryAfter extends SyncRoundOutcome {
  const SyncRoundRetryAfter(this.delay);

  final Duration delay;

  @override
  bool operator ==(Object other) =>
      other is SyncRoundRetryAfter && other.delay == delay;

  @override
  int get hashCode => delay.hashCode;

  @override
  String toString() => 'SyncRoundRetryAfter($delay)';
}

/// A transient failure: no network, a timeout, an unreadable response or a
/// status without a specific reaction. The caller backs off and retries.
class SyncRoundFailed extends SyncRoundOutcome {
  const SyncRoundFailed();
}

class SyncRoundAccountGone extends SyncRoundOutcome {
  const SyncRoundAccountGone();
}

class SyncRoundAppVersionUnsupported extends SyncRoundOutcome {
  const SyncRoundAppVersionUnsupported();
}
