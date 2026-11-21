import 'package:dio/dio.dart';
import 'package:drift/drift.dart';
import 'package:uuid/uuid.dart';

import '../../core/sync/app_database.dart';

class ShoppingListRepository {
  ShoppingListRepository({
    required this.database,
    required this.apiClient,
    required this.fetchJsonWebToken,
  });

  final AppDatabase database;
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

  Future<void> ensureList(String defaultName) => database.transaction(() async {
    if (await _firstListQuery().getSingleOrNull() != null) {
      return;
    }
    final now = _now();
    await database
        .into(database.shoppingLists)
        .insert(
          ShoppingListsCompanion.insert(
            id: const Uuid().v4(),
            name: defaultName,
            createdAt: now,
            updatedAt: now,
          ),
        );
  });

  Future<void> addItem(ShoppingList list, String name) async {
    final now = _now();
    await database
        .into(database.shoppingListItems)
        .insert(
          ShoppingListItemsCompanion.insert(
            id: const Uuid().v4(),
            listId: list.id,
            name: name,
            checked: 0,
            createdAt: now,
            updatedAt: now,
          ),
        );
  }

  Future<void> toggleChecked(ShoppingListItem item) async {
    await (database.update(
      database.shoppingListItems,
    )..where((row) => row.id.equals(item.id))).write(
      ShoppingListItemsCompanion(
        checked: Value(item.checked == 0 ? 1 : 0),
        updatedAt: Value(_now()),
      ),
    );
  }

  Future<void> deleteItem(ShoppingListItem item) async {
    await (database.delete(
      database.shoppingListItems,
    )..where((row) => row.id.equals(item.id))).go();
  }

  Future<void> shareList(ShoppingList list, String userId) async {
    await apiClient.post<void>(
      '/v1/lists/${list.id}/members',
      data: {'userId': userId},
      options: await _authorization(),
    );
  }

  Future<void> leaveList(ShoppingList list, String ownUserId) async {
    await apiClient.delete<void>(
      '/v1/lists/${list.id}/members/$ownUserId',
      options: await _authorization(),
    );
  }

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

  String _now() => DateTime.now().toUtc().toIso8601String();
}
