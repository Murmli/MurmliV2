import 'dart:convert';

import 'package:drift/drift.dart';

import 'app_database.dart';

enum SyncCommandType {
  createList,
  addItem,
  renameItem,
  setItemChecked,
  deleteItem,
}

class SyncCommand {
  const SyncCommand({
    required this.id,
    required this.type,
    required this.arguments,
    required this.occurredAt,
  });

  factory SyncCommand.fromPending(PendingCommand command) => SyncCommand(
    id: command.id,
    type: SyncCommandType.values.byName(command.type),
    arguments: jsonDecode(command.argumentsJson) as Map<String, Object?>,
    occurredAt: command.occurredAt,
  );

  final String id;
  final SyncCommandType type;
  final Map<String, Object?> arguments;
  final DateTime occurredAt;

  PendingCommandsCompanion toPending() => PendingCommandsCompanion.insert(
    id: id,
    type: type.name,
    argumentsJson: jsonEncode(arguments),
    occurredAt: occurredAt,
  );

  Map<String, Object?> toJson() => {
    'id': id,
    'type': type.name,
    'occurredAt': occurredAt.toUtc().toIso8601String(),
    'arguments': arguments,
  };
}

// Used for optimistic local writes and to rebase still pending commands onto
// the state pulled from the API.
Future<void> applySyncCommand(AppDatabase database, SyncCommand command) async {
  final arguments = command.arguments;
  final occurredAt = command.occurredAt;
  switch (command.type) {
    case SyncCommandType.createList:
      await database
          .into(database.shoppingLists)
          .insert(
            ShoppingListsCompanion.insert(
              id: arguments['listId']! as String,
              name: arguments['name']! as String,
              createdAt: occurredAt,
              updatedAt: occurredAt,
            ),
            mode: InsertMode.insertOrIgnore,
          );
    case SyncCommandType.addItem:
      await database
          .into(database.shoppingListItems)
          .insert(
            ShoppingListItemsCompanion.insert(
              id: arguments['itemId']! as String,
              listId: arguments['listId']! as String,
              name: arguments['name']! as String,
              quantity: Value((arguments['quantity'] as num?)?.toDouble()),
              unit: Value(arguments['unit'] as String?),
              category: Value(arguments['category'] as String?),
              checked: false,
              createdAt: occurredAt,
              updatedAt: occurredAt,
            ),
            mode: InsertMode.insertOrIgnore,
          );
    case SyncCommandType.renameItem:
      await _updateItem(
        database,
        arguments['itemId']! as String,
        ShoppingListItemsCompanion(
          name: Value(arguments['name']! as String),
          updatedAt: Value(occurredAt),
        ),
      );
    case SyncCommandType.setItemChecked:
      await _updateItem(
        database,
        arguments['itemId']! as String,
        ShoppingListItemsCompanion(
          checked: Value(arguments['checked']! as bool),
          updatedAt: Value(occurredAt),
        ),
      );
    case SyncCommandType.deleteItem:
      await (database.delete(
        database.shoppingListItems,
      )..where((item) => item.id.equals(arguments['itemId']! as String))).go();
  }
}

Future<void> _updateItem(
  AppDatabase database,
  String itemId,
  ShoppingListItemsCompanion changes,
) => (database.update(
  database.shoppingListItems,
)..where((item) => item.id.equals(itemId))).write(changes);
