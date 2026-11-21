import 'dart:convert';

import 'package:app/app/core/sync/app_database.dart';

import 'fault_injecting_http_client_adapter.dart';
import 'simulated_device.dart';

class EnqueuedCommand {
  EnqueuedCommand(
    PendingCommand pending, {
    required this.device,
    required this.step,
  }) : id = pending.id,
       type = pending.type,
       arguments = jsonDecode(pending.argumentsJson) as Map<String, Object?>,
       occurredAt = pending.occurredAt;

  final String id;
  final String type;
  final Map<String, Object?> arguments;
  final DateTime occurredAt;
  final SimulatedDevice device;
  final int step;

  String? get listId => arguments['listId'] as String?;

  String? get itemId => arguments['itemId'] as String?;
}

typedef Verdict = ({String status, String? reason, int position});

/// Expected server state derived only from the answers the server gave.
class ServerLedger {
  final commands = <String, EnqueuedCommand>{};
  final verdicts = <String, Verdict>{};
  final nonDuplicateResultCounts = <String, int>{};
  final membersByList = <String, Set<SimulatedUser>>{};
  final _listNames = <String, String>{};
  final _itemNames = <String, String>{};

  void enqueue(EnqueuedCommand command) {
    commands[command.id] = command;
    switch (command.type) {
      case 'createList':
        _listNames[command.listId!] = command.arguments['name']! as String;
      case 'addItem':
        _itemNames[command.itemId!] = command.arguments['name']! as String;
    }
  }

  void recordSyncResponse(RecordedSyncResponse response) {
    for (final result in _resultsOf(response)) {
      final id = result['id']! as String;
      final status = result['status']! as String;
      if (status == 'duplicate') {
        continue;
      }
      nonDuplicateResultCounts.update(
        id,
        (count) => count + 1,
        ifAbsent: () => 1,
      );
      if (verdicts.containsKey(id)) {
        continue;
      }
      verdicts[id] = (
        status: status,
        reason: result['reason'] as String?,
        position: verdicts.length,
      );
      final command = commands[id];
      if (command != null &&
          command.type == 'createList' &&
          status == 'applied') {
        recordMemberAdded(command.listId!, command.device.user);
      }
    }
  }

  void recordMemberAdded(String listId, SimulatedUser user) =>
      membersByList.putIfAbsent(listId, () => {}).add(user);

  void recordMemberRemoved(String listId, SimulatedUser user) {
    final members = membersByList[listId];
    if (members == null) {
      return;
    }
    members.remove(user);
    if (members.isEmpty) {
      membersByList.remove(listId);
    }
  }

  bool isApplied(EnqueuedCommand command) =>
      verdicts[command.id]?.status == 'applied';

  Set<String> expectedItemIdsOf(String listId) =>
      {
        for (final command in commands.values)
          if (command.type == 'addItem' &&
              command.listId == listId &&
              isApplied(command))
            command.itemId!,
      }.difference({
        for (final command in commands.values)
          if (command.type == 'deleteItem' && isApplied(command))
            command.itemId!,
      });

  String listLabel(String listId) => _listNames[listId] ?? listId;

  String itemLabel(String itemId) => _itemNames[itemId] ?? itemId;

  String describe(EnqueuedCommand command) => switch (command.type) {
    'createList' => 'createList ${command.arguments['name']}',
    'addItem' =>
      'addItem ${command.arguments['name']} to ${listLabel(command.listId!)}',
    'renameItem' =>
      'renameItem ${itemLabel(command.itemId!)} to ${command.arguments['name']}',
    'setItemChecked' =>
      'setItemChecked ${itemLabel(command.itemId!)} to '
          '${command.arguments['checked']}',
    'deleteItem' => 'deleteItem ${itemLabel(command.itemId!)}',
    _ => '${command.type} ${command.arguments}',
  };

  String describeResponse(RecordedSyncResponse? response) {
    if (response == null) {
      return 'no response';
    }
    final body = response.body;
    if (body == null) {
      return 'HTTP ${response.statusCode}';
    }
    final results = [
      for (final result in _resultsOf(response))
        '${_describeCommandId(result['id']! as String)}: ${result['status']}'
            '${result['reason'] == null ? '' : ' (${result['reason']})'}',
    ];
    return 'pulled ${(body['lists']! as List<Object?>).length} lists, '
        '${(body['items']! as List<Object?>).length} items, '
        '${(body['removedListIds']! as List<Object?>).length} removed lists; '
        'results: ${results.isEmpty ? 'none' : results.join(', ')}';
  }

  String _describeCommandId(String id) => switch (commands[id]) {
    final command? => describe(command),
    null => 'unknown command $id',
  };

  List<Map<String, Object?>> _resultsOf(RecordedSyncResponse response) =>
      (response.body?['results'] as List<Object?>? ?? [])
          .cast<Map<String, Object?>>();
}
