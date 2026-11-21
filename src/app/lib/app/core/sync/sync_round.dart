import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:talker/talker.dart';

import 'app_database.dart';
import 'sync_command.dart';

enum SyncRoundOutcome { completed, cancelled, commandsDiscarded, failed }

class SyncRound {
  SyncRound({
    required this.database,
    required this.client,
    required this.fetchJsonWebToken,
    required this.talker,
  });

  final AppDatabase database;
  final Dio client;
  final Future<String?> Function() fetchJsonWebToken;
  final Talker talker;

  Future<SyncRoundOutcome> run({CancelToken? cancelToken}) async {
    var sentCommands = <PendingCommand>[];
    try {
      final jsonWebToken = await fetchJsonWebToken();
      if (jsonWebToken == null) {
        talker.warning('Sync failed: no token');
        return SyncRoundOutcome.failed;
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
      return SyncRoundOutcome.completed;
    } on DioException catch (error) {
      if (CancelToken.isCancel(error)) {
        return SyncRoundOutcome.cancelled;
      }
      if (error.response?.statusCode == 400 && sentCommands.isNotEmpty) {
        // A malformed request never succeeds, retrying would block every later
        // change. A full snapshot repairs the optimistic local state.
        talker.warning('Sync discarded the pending commands after a 400');
        await database.transaction(() async {
          await database.delete(database.pendingCommands).go();
          await database.writeSyncToken(null);
        });
        return SyncRoundOutcome.commandsDiscarded;
      }
      talker.warning(
        'Sync failed: ${error.response?.statusCode ?? error.type.name}',
      );
      return SyncRoundOutcome.failed;
    } on Exception catch (error, stackTrace) {
      talker.handle(error, stackTrace, 'Sync failed');
      return SyncRoundOutcome.failed;
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

    for (final result in results) {
      final status = result['status']! as String;
      if (status == 'rejected' || status == 'ignored') {
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

      await database.writeSyncToken(body['token']! as String);
    });
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

  Options _authorizedBy(String jsonWebToken) =>
      Options(headers: {'Authorization': 'Bearer $jsonWebToken'});
}
