import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:talker/talker.dart';

import 'app_database.dart';
import 'sync_command.dart';

typedef SyncEngineStatus = ({bool isConnected, bool isSyncing, bool isOffline});

const stoppedSyncEngineStatus = (
  isConnected: false,
  isSyncing: false,
  isOffline: true,
);

class SyncEngine {
  SyncEngine({
    required this.database,
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

  final AppDatabase database;
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
    var sentCommands = <PendingCommand>[];
    try {
      final jsonWebToken = await fetchJsonWebToken();
      if (jsonWebToken == null) {
        talker.warning('Sync failed: no token');
        _updateStatus(isSyncing: false, isOffline: true);
        return false;
      }
      sentCommands = await _readPendingCommands();
      final requestToken = await database.readSyncToken();
      final response = await client.post<Map<String, Object?>>(
        '/v1/sync',
        data: {
          'token': requestToken,
          'commands': [
            for (final command in sentCommands)
              SyncCommand.fromPending(command).toJson(),
          ],
        },
        options: _authorizedBy(jsonWebToken),
        cancelToken: cancelToken,
      );
      await _applyResponse(
        response.data!,
        requestToken: requestToken,
        sentCommands: sentCommands,
      );
      _failedSyncCount = 0;
      _updateStatus(isSyncing: false, isOffline: false);
      return true;
    } on DioException catch (error) {
      if (CancelToken.isCancel(error)) {
        return true;
      }
      if (error.response?.statusCode == 400 && sentCommands.isNotEmpty) {
        // A malformed request never succeeds, retrying would block every later
        // change. A full snapshot repairs the optimistic local state.
        talker.warning('Sync discarded the pending commands after a 400');
        await database.transaction(() async {
          await database.delete(database.pendingCommands).go();
          await database.writeSyncToken(null);
        });
        _isSyncRequestedAgain = true;
        _updateStatus(isSyncing: false);
        return true;
      }
      talker.warning(
        'Sync failed: ${error.response?.statusCode ?? error.type.name}',
      );
      _updateStatus(isSyncing: false, isOffline: true);
      return false;
    } on Exception catch (error, stackTrace) {
      talker.handle(error, stackTrace, 'Sync failed');
      _updateStatus(isSyncing: false, isOffline: true);
      return false;
    }
  }

  Future<void> _applyResponse(
    Map<String, Object?> body, {
    required String? requestToken,
    required List<PendingCommand> sentCommands,
  }) async {
    final results = (body['results']! as List<Object?>)
        .cast<Map<String, Object?>>();
    final lists = (body['lists']! as List<Object?>)
        .cast<Map<String, Object?>>();
    final items = (body['items']! as List<Object?>)
        .cast<Map<String, Object?>>();
    final removedListIds = (body['removedListIds']! as List<Object?>)
        .cast<String>();
    final commandTypes = {
      for (final command in sentCommands) command.id: command.type,
    };

    var isFullSnapshotNeeded = false;
    for (final result in results) {
      final status = result['status']! as String;
      if (status == 'rejected' || status == 'ignored') {
        // The pull only contains rows changed since the token, it would not
        // revert the optimistic local change of a discarded command.
        isFullSnapshotNeeded = true;
        talker.info(
          'Sync command ${commandTypes[result['id']]} $status: '
          '${result['reason'] ?? 'no reason'}',
        );
      }
    }

    await database.transaction(() async {
      for (final list in lists) {
        await _applyList(list);
      }
      for (final item in items) {
        await _applyItem(item);
      }
      for (final listId in removedListIds) {
        await _deleteLists((id) => id.equals(listId));
      }
      if (requestToken == null) {
        final listIds = [for (final list in lists) list['id']! as String];
        await _deleteLists((id) => id.isNotIn(listIds));
      }

      await (database.delete(database.pendingCommands)..where(
            (command) => command.id.isIn([
              for (final result in results) result['id']! as String,
            ]),
          ))
          .go();
      for (final command in await _readPendingCommands()) {
        await applySyncCommand(database, SyncCommand.fromPending(command));
      }

      await database.writeSyncToken(
        isFullSnapshotNeeded ? null : body['token']! as String,
      );
    });

    if (isFullSnapshotNeeded) {
      _isSyncRequestedAgain = true;
    }
  }

  Future<List<PendingCommand>> _readPendingCommands() => (database.select(
    database.pendingCommands,
  )..orderBy([(command) => OrderingTerm.asc(command.sequence)])).get();

  Future<void> _applyList(Map<String, Object?> list) async {
    final listId = list['id']! as String;
    await database
        .into(database.shoppingLists)
        .insertOnConflictUpdate(
          ShoppingListsCompanion.insert(
            id: listId,
            name: list['name']! as String,
            createdAt: DateTime.parse(list['createdAt']! as String),
            updatedAt: DateTime.parse(list['updatedAt']! as String),
          ),
        );
    await (database.delete(
      database.shoppingListMembers,
    )..where((member) => member.listId.equals(listId))).go();
    for (final member
        in (list['members']! as List<Object?>).cast<Map<String, Object?>>()) {
      await database
          .into(database.shoppingListMembers)
          .insertOnConflictUpdate(
            ShoppingListMembersCompanion.insert(
              id: member['id']! as String,
              listId: listId,
              userId: member['userId']! as String,
              createdAt: DateTime.parse(member['createdAt']! as String),
            ),
          );
    }
    if (list['snapshot'] == true) {
      await (database.delete(
        database.shoppingListItems,
      )..where((item) => item.listId.equals(listId))).go();
    }
  }

  Future<void> _applyItem(Map<String, Object?> item) async {
    final itemId = item['id']! as String;
    if (item['deleted'] == true) {
      await (database.delete(
        database.shoppingListItems,
      )..where((row) => row.id.equals(itemId))).go();
      return;
    }
    await database
        .into(database.shoppingListItems)
        .insertOnConflictUpdate(
          ShoppingListItemsCompanion.insert(
            id: itemId,
            listId: item['listId']! as String,
            name: item['name']! as String,
            quantity: Value((item['quantity'] as num?)?.toDouble()),
            unit: Value(item['unit'] as String?),
            category: Value(item['category'] as String?),
            checked: item['checked']! as bool,
            createdAt: DateTime.parse(item['createdAt']! as String),
            updatedAt: DateTime.parse(item['updatedAt']! as String),
          ),
        );
  }

  Future<void> _deleteLists(
    Expression<bool> Function(GeneratedColumn<String> listId) matches,
  ) async {
    await (database.delete(
      database.shoppingListItems,
    )..where((item) => matches(item.listId))).go();
    await (database.delete(
      database.shoppingListMembers,
    )..where((member) => matches(member.listId))).go();
    await (database.delete(
      database.shoppingLists,
    )..where((list) => matches(list.id))).go();
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

  Options _authorizedBy(String jsonWebToken) =>
      Options(headers: {'Authorization': 'Bearer $jsonWebToken'});

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
