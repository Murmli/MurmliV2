/// What [SyncScheduler] is doing, for debugging only; the app shows no
/// offline or pending indicator.
enum SyncActivity {
  /// Signed out, or stopped after `accountGone` or `appVersionUnsupported`.
  stopped,

  /// In the background, where no timer runs.
  paused,
  idle,
  syncing,

  /// Waiting a random part of the next step after failed rounds.
  backingOff,

  /// Waiting the time the server asked for with `Retry-After`.
  waitingForServer,

  /// The system reports no network.
  waitingForNetwork,
}
