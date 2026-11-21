import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:uuid/uuid.dart';

import '../../core/sync/app_database.dart';
import '../../core/sync/sync_command.dart';

class ShoppingListRepository {
  ShoppingListRepository({
    required this.database,
    required this.requestSync,
    required this.apiClient,
    required this.fetchJsonWebToken,
  });

  final AppDatabase database;
  final void Function({bool isDebounced}) requestSync;
  final Dio apiClient;
  final Future<String?> Function() fetchJsonWebToken;

  Stream<ShoppingList?> watchFirstList() =>
      _firstListQuery().watchSingleOrNull();

  Stream<List<ShoppingListItem>> watchItems(String listId) =>
      (database.select(database.shoppingListItems)
            ..where((item) => item.listId.equals(listId))
            ..orderBy([
              (item) => OrderingTerm.asc(item.checked),
              (item) => OrderingTerm.asc(item.createdAt),
            ]))
          .watch();

  Future<void> ensureList(String defaultName) async {
    final isCreated = await database.transaction(() async {
      final hasSynced = await database.readSyncToken() != null;
      if (!hasSynced ||
          await _firstListQuery().getSingleOrNull() != null ||
          await _hasPendingCreateList()) {
        return false;
      }
      await _record(SyncCommandType.createList, {
        'listId': const Uuid().v4(),
        'name': defaultName,
      });
      return true;
    });
    if (isCreated) {
      requestSync();
    }
  }

  Future<void> addItem(ShoppingList list, String name) => _write(
    SyncCommandType.addItem,
    {'itemId': const Uuid().v4(), 'listId': list.id, 'name': name},
  );

  Future<void> toggleChecked(ShoppingListItem item) => _write(
    SyncCommandType.setItemChecked,
    {'itemId': item.id, 'checked': !item.checked},
  );

  Future<void> renameItem(ShoppingListItem item, String name) async {
    final trimmedName = name.trim();
    if (trimmedName.isEmpty || trimmedName == item.name) {
      return;
    }
    await _write(SyncCommandType.renameItem, {
      'itemId': item.id,
      'name': trimmedName,
    });
  }

  Future<void> deleteItem(ShoppingListItem item) =>
      _write(SyncCommandType.deleteItem, {'itemId': item.id});

  Future<void> shareList(ShoppingList list, String userId) async {
    await apiClient.post<void>(
      '/v1/lists/${list.id}/members',
      data: {'userId': userId},
      options: await _authorization(),
    );
    requestSync();
  }

  Future<void> leaveList(ShoppingList list, String ownUserId) async {
    await apiClient.delete<void>(
      '/v1/lists/${list.id}/members/$ownUserId',
      options: await _authorization(),
    );
    requestSync();
  }

  Future<void> _write(
    SyncCommandType type,
    Map<String, Object?> arguments,
  ) async {
    await database.transaction(() => _record(type, arguments));
    requestSync(isDebounced: true);
  }

  Future<void> _record(
    SyncCommandType type,
    Map<String, Object?> arguments,
  ) async {
    final command = SyncCommand(
      id: const Uuid().v4(),
      type: type,
      arguments: arguments,
      occurredAt: DateTime.now().toUtc(),
    );
    await applySyncCommand(database, command);
    await database.into(database.pendingCommands).insert(command.toPending());
  }

  Future<bool> _hasPendingCreateList() async =>
      await (database.select(database.pendingCommands)
            ..where(
              (command) => command.type.equals(SyncCommandType.createList.name),
            )
            ..limit(1))
          .getSingleOrNull() !=
      null;

  SimpleSelectStatement<$ShoppingListsTable, ShoppingList> _firstListQuery() =>
      database.select(database.shoppingLists)
        ..orderBy([(list) => OrderingTerm.asc(list.createdAt)])
        ..limit(1);

  Future<Options> _authorization() async {
    final jsonWebToken = await fetchJsonWebToken();
    if (jsonWebToken == null) {
      throw StateError('Cannot call the API while signed out.');
    }
    return Options(headers: {'Authorization': 'Bearer $jsonWebToken'});
  }
}
