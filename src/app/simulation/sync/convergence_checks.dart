import 'package:app/app/core/sync/app_database.dart';

import 'server_ledger.dart';
import 'simulated_device.dart';

typedef CheckViolation = ({String check, List<String> details});

/// The rows of the lists, members and items only in [expected] (-) or only in
/// [actual] (+), or null if both match.
String? rowDifferenceOf({
  required DeviceTables expected,
  required DeviceTables actual,
  required ServerLedger ledger,
  required List<SimulatedUser> users,
}) => _differenceOf(
  expected: _rowsOf(expected, ledger, users),
  actual: _rowsOf(actual, ledger, users),
);

enum _ItemField {
  name(changeType: 'renameItem'),
  checked(changeType: 'setItemChecked');

  const _ItemField({required this.changeType});

  final String changeType;

  bool isChangedBy(EnqueuedCommand command) =>
      command.type == 'addItem' || command.type == changeType;

  Object? valueSetBy(EnqueuedCommand command) => switch (this) {
    name => command.arguments['name'],
    checked => command.type == 'addItem' ? false : command.arguments['checked'],
  };

  Object valueOf(ShoppingListItem item) => switch (this) {
    name => item.name,
    checked => item.checked,
  };
}

class ConvergenceChecks {
  ConvergenceChecks({
    required this.ledger,
    required this.users,
    required this.deviceTables,
    required this.referenceTables,
    required this.discardedRounds,
  });

  final ServerLedger ledger;
  final List<SimulatedUser> users;
  final Map<SimulatedDevice, DeviceTables> deviceTables;
  final Map<SimulatedUser, DeviceTables> referenceTables;
  final List<String> discardedRounds;

  List<CheckViolation> run({required bool includesLastWriterWins}) => [
    ?_violation('check 1 (convergence)', _convergence()),
    ?_violation('check 2 (queue)', _queue()),
    ?_violation('check 3 (verdicts)', _verdicts()),
    ?_violation('check 4 (visibility)', _visibility()),
    ?_violation('check 5 (items)', _items()),
    if (includesLastWriterWins)
      ?_violation('check 6 (last writer wins)', _lastWriterWins()),
  ];

  List<String> _convergence() => [
    for (final MapEntry(key: device, value: tables) in deviceTables.entries)
      if (rowDifferenceOf(
            expected: referenceTables[device.user]!,
            actual: tables,
            ledger: ledger,
            users: users,
          )
          case final difference?)
        'device ${device.name} differs from the reference of user '
            '${device.user.name} (- only in the reference, + only on the '
            'device):\n$difference',
  ];

  List<String> _queue() => [
    for (final MapEntry(key: device, value: tables) in deviceTables.entries)
      for (final pending in tables.pendingCommands)
        'device ${device.name} still has the pending command '
            '${_describePending(pending)}',
    for (final round in discardedRounds)
      'the round of $round ended with commandsDiscarded',
  ];

  List<String> _verdicts() => [
    for (final command in ledger.commands.values)
      if (ledger.nonDuplicateResultCounts[command.id] ?? 0 case final count
          when count != 1)
        '${ledger.describe(command)} (device ${command.device.name}, step '
            '${command.step}) has $count non-duplicate verdicts',
    for (final id in ledger.nonDuplicateResultCounts.keys)
      if (!ledger.commands.containsKey(id))
        'the server answered the unknown command $id',
  ];

  List<String> _visibility() => [
    for (final user in users)
      if (_differenceOf(
            expected: [
              for (final MapEntry(key: listId, value: members)
                  in ledger.membersByList.entries)
                if (members.contains(user)) ledger.listLabel(listId),
            ],
            actual: [
              for (final list in referenceTables[user]!.lists)
                ledger.listLabel(list.id),
            ],
          )
          case final difference?)
        'lists of user ${user.name} (- member according to the server '
            'answers but missing, + shown without membership):\n$difference',
  ];

  List<String> _items() => [
    for (final MapEntry(key: listId, value: members)
        in ledger.membersByList.entries)
      for (final user in members)
        if (_differenceOf(
              expected: ledger.expectedItemIdsOf(listId).map(ledger.itemLabel),
              actual: [
                for (final item in referenceTables[user]!.items)
                  if (item.listId == listId) ledger.itemLabel(item.id),
              ],
            )
            case final difference?)
          'items of ${ledger.listLabel(listId)} for user ${user.name} (- '
              'applied addItem without applied deleteItem but missing, + '
              'shown without that):\n$difference',
  ];

  List<String> _lastWriterWins() {
    final details = <String>[];
    for (final addition in ledger.commands.values) {
      if (addition.type != 'addItem' || !ledger.isApplied(addition)) {
        continue;
      }
      for (final field in _ItemField.values) {
        final expectedValue = _walkChanges(addition.itemId!, field, details);
        for (final MapEntry(key: user, value: tables)
            in referenceTables.entries) {
          for (final item in tables.items) {
            if (item.id == addition.itemId &&
                field.valueOf(item) != expectedValue) {
              details.add(
                '${field.name} of ${ledger.itemLabel(item.id)} is '
                '${field.valueOf(item)} in the reference of user ${user.name}, '
                'the last applied command set $expectedValue',
              );
            }
          }
        }
      }
    }
    return details;
  }

  Object? _walkChanges(String itemId, _ItemField field, List<String> details) {
    final changes =
        [
          for (final command in ledger.commands.values)
            if (command.itemId == itemId && field.isChangedBy(command))
              if (ledger.verdicts[command.id] case final verdict?)
                (command: command, verdict: verdict),
        ]..sort(
          (first, second) =>
              first.verdict.position.compareTo(second.verdict.position),
        );
    DateTime? current;
    Object? value;
    for (final (:command, :verdict) in changes) {
      final isOlder = current != null && command.occurredAt.isBefore(current);
      final description =
          '${ledger.describe(command)} (device ${command.device.name}, step '
          '${command.step}, occurredAt ${_instant(command.occurredAt)}, '
          '${field.name} changed at ${current == null ? 'never' : _instant(current)})';
      if (verdict.status == 'applied') {
        if (isOlder) {
          details.add('$description was applied although it is older');
        }
        current = command.occurredAt;
        value = field.valueSetBy(command);
      } else if (verdict.status == 'ignored' &&
          verdict.reason == 'stale' &&
          !isOlder) {
        details.add(
          '$description was ignored as stale although it is not older',
        );
      }
    }
    return value;
  }

  String _describePending(PendingCommand pending) =>
      switch (ledger.commands[pending.id]) {
        final command? => ledger.describe(command),
        null => '${pending.type} ${pending.argumentsJson}',
      };

  static CheckViolation? _violation(String check, List<String> details) =>
      details.isEmpty ? null : (check: check, details: details);
}

List<String> _rowsOf(
  DeviceTables tables,
  ServerLedger ledger,
  List<SimulatedUser> users,
) => [
  for (final list in tables.lists)
    'list ${ledger.listLabel(list.id)} name=${list.name} '
        'createdAt=${_instant(list.createdAt)} '
        'updatedAt=${_instant(list.updatedAt)}',
  for (final member in tables.members)
    'member ${member.id} list=${ledger.listLabel(member.listId)} '
        'user=${_userName(member.userId, users)} '
        'createdAt=${_instant(member.createdAt)}',
  for (final item in tables.items)
    'item ${ledger.itemLabel(item.id)} list=${ledger.listLabel(item.listId)} '
        'name=${item.name} quantity=${item.quantity} unit=${item.unit} '
        'category=${item.category} checked=${item.checked} '
        'createdAt=${_instant(item.createdAt)} '
        'updatedAt=${_instant(item.updatedAt)}',
];

String _userName(String userId, List<SimulatedUser> users) =>
    users
        .where((user) => user.id == userId)
        .map((user) => user.name)
        .firstOrNull ??
    userId;

String? _differenceOf({
  required Iterable<String> expected,
  required Iterable<String> actual,
}) {
  final expectedRows = expected.toSet();
  final actualRows = actual.toSet();
  final difference = [
    for (final row in expectedRows.difference(actualRows).toList()..sort())
      '  - $row',
    for (final row in actualRows.difference(expectedRows).toList()..sort())
      '  + $row',
  ];
  return difference.isEmpty ? null : difference.join('\n');
}

String _instant(DateTime dateTime) => dateTime.toUtc().toIso8601String();
