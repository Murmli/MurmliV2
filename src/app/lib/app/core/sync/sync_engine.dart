import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:talker/talker.dart';

import 'sync_round.dart';

typedef SyncEngineStatus = ({bool isConnected, bool isSyncing, bool isOffline});

const stoppedSyncEngineStatus = (
  isConnected: false,
  isSyncing: false,
  isOffline: true,
);

class SyncEngine {
  SyncEngine({
    required this.round,
    required this.client,
    required this.fetchJsonWebToken,
    required this.talker,
  });

  static const _localWriteDebounce = Duration(milliseconds: 300);
  static const _fallbackInterval = Duration(seconds: 60);
  static const _eventStreamIdleTimeout = Duration(seconds: 60);
  static const _retryDelays = [
    Duration(seconds: 1),
    Duration(seconds: 2),
    Duration(seconds: 5),
    Duration(seconds: 10),
    Duration(seconds: 30),
  ];

  final SyncRound round;
  final Dio client;
  final Future<String?> Function() fetchJsonWebToken;
  final Talker talker;

  final _statusController = StreamController<SyncEngineStatus>.broadcast();
  SyncEngineStatus _status = stoppedSyncEngineStatus;

  CancelToken? _cancelToken;
  Future<void>? _activeSync;
  bool _isSyncRequestedAgain = false;
  int _failedSyncCount = 0;
  int _failedEventStreamCount = 0;
  Timer? _debounceTimer;
  Timer? _retryTimer;
  Timer? _reconnectTimer;
  Timer? _fallbackTimer;
  StreamSubscription<String>? _eventSubscription;

  bool get _isRunning => _cancelToken != null;

  Stream<SyncEngineStatus> watchStatus() async* {
    yield _status;
    yield* _statusController.stream;
  }

  void start() {
    if (_isRunning) {
      return;
    }
    _cancelToken = CancelToken();
    _updateStatus(isOffline: false);
    _fallbackTimer = Timer.periodic(_fallbackInterval, (_) => requestSync());
    unawaited(_connectEventStream());
    requestSync();
  }

  Future<void> stop() async {
    _halt();
    await _activeSync;
  }

  void dispose() {
    _halt();
    unawaited(_statusController.close());
    client.close(force: true);
  }

  void requestSync({bool isDebounced = false}) {
    if (!_isRunning) {
      return;
    }
    _debounceTimer?.cancel();
    if (isDebounced) {
      _debounceTimer = Timer(_localWriteDebounce, requestSync);
      return;
    }
    if (_activeSync != null) {
      _isSyncRequestedAgain = true;
      return;
    }
    _retryTimer?.cancel();
    _activeSync = _syncUntilSettled().whenComplete(() => _activeSync = null);
  }

  void _halt() {
    _cancelToken?.cancel();
    _cancelToken = null;
    for (final timer in [
      _debounceTimer,
      _retryTimer,
      _reconnectTimer,
      _fallbackTimer,
    ]) {
      timer?.cancel();
    }
    unawaited(_eventSubscription?.cancel());
    _eventSubscription = null;
    _isSyncRequestedAgain = false;
    _failedSyncCount = 0;
    _failedEventStreamCount = 0;
    _updateStatus(isConnected: false, isSyncing: false, isOffline: true);
  }

  Future<void> _syncUntilSettled() async {
    do {
      _isSyncRequestedAgain = false;
      final cancelToken = _cancelToken;
      if (cancelToken == null) {
        return;
      }
      if (!await _sync(cancelToken)) {
        if (cancelToken.isCancelled) {
          continue;
        }
        _retryTimer = Timer(_delayAfter(++_failedSyncCount), requestSync);
        return;
      }
    } while (_isSyncRequestedAgain);
  }

  Future<bool> _sync(CancelToken cancelToken) async {
    _updateStatus(isSyncing: true);
    switch (await round.run(cancelToken: cancelToken)) {
      case SyncRoundOutcome.completed:
        _failedSyncCount = 0;
        _updateStatus(
          isSyncing: false,
          isOffline: cancelToken.isCancelled ? null : false,
        );
        return true;
      case SyncRoundOutcome.cancelled:
        return true;
      case SyncRoundOutcome.commandsDiscarded:
        _isSyncRequestedAgain = true;
        _updateStatus(isSyncing: false);
        return true;
      case SyncRoundOutcome.failed:
        _updateStatus(isSyncing: false, isOffline: true);
        return false;
    }
  }

  Future<void> _connectEventStream() async {
    final cancelToken = _cancelToken;
    if (cancelToken == null) {
      return;
    }
    try {
      final jsonWebToken = await fetchJsonWebToken();
      if (jsonWebToken == null) {
        talker.warning('Sync event stream failed: no token');
      } else {
        await _listenToEventStream(jsonWebToken, cancelToken);
      }
    } on DioException catch (error) {
      if (!CancelToken.isCancel(error)) {
        talker.warning(
          'Sync event stream failed: '
          '${error.response?.statusCode ?? error.type.name}',
        );
      }
    } on Object catch (error) {
      talker.warning('Sync event stream failed: ${error.runtimeType}');
    }
    if (cancelToken.isCancelled) {
      return;
    }
    _eventSubscription = null;
    _updateStatus(isConnected: false);
    _reconnectTimer = Timer(
      _delayAfter(++_failedEventStreamCount),
      () => unawaited(_connectEventStream()),
    );
  }

  Future<void> _listenToEventStream(
    String jsonWebToken,
    CancelToken cancelToken,
  ) async {
    final response = await client.get<ResponseBody>(
      '/v1/sync/events',
      options: Options(
        responseType: ResponseType.stream,
        headers: {
          'Authorization': 'Bearer $jsonWebToken',
          'Accept': 'text/event-stream',
        },
      ),
      cancelToken: cancelToken,
    );
    if (cancelToken.isCancelled) {
      return;
    }
    _failedEventStreamCount = 0;
    _updateStatus(isConnected: true);
    requestSync();
    final ended = Completer<void>();
    unawaited(cancelToken.whenCancel.then((_) => _complete(ended)));
    _eventSubscription = response.data!.stream
        // Keepalives arrive every 25 s, silence means a dead connection.
        .timeout(_eventStreamIdleTimeout)
        .cast<List<int>>()
        .transform(utf8.decoder)
        .transform(const LineSplitter())
        .listen(
          (line) {
            if (line == 'event: poke') {
              requestSync();
            }
          },
          onError: (Object error) {
            if (!ended.isCompleted) {
              ended.completeError(error);
            }
          },
          onDone: () => _complete(ended),
          cancelOnError: true,
        );
    await ended.future;
    if (!cancelToken.isCancelled) {
      talker.info('Sync event stream ended');
    }
  }

  void _complete(Completer<void> completer) {
    if (!completer.isCompleted) {
      completer.complete();
    }
  }

  Duration _delayAfter(int failureCount) =>
      _retryDelays[(failureCount - 1).clamp(0, _retryDelays.length - 1)];

  void _updateStatus({bool? isConnected, bool? isSyncing, bool? isOffline}) {
    _status = (
      isConnected: isConnected ?? _status.isConnected,
      isSyncing: isSyncing ?? _status.isSyncing,
      isOffline: isOffline ?? _status.isOffline,
    );
    if (!_statusController.isClosed) {
      _statusController.add(_status);
    }
  }
}
