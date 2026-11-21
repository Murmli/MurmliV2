import 'dart:async';
import 'dart:math';

import 'package:app/app/core/sync/app_database.dart';
import 'package:app/app/core/sync/sync_round.dart';
import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import 'convergence_checks.dart';
import 'fault_injecting_http_client_adapter.dart';
import 'json_web_token.dart';
import 'server_ledger.dart';
import 'simulated_device.dart';
import 'simulation_configuration.dart';

enum SimulationMode { sequential, parallel, overlapping }

enum _Action {
  ensureList(3),
  addItem(6),
  renameItem(4),
  toggleChecked(4),
  deleteItem(2),
  shareList(2),
  leaveList(1),
  sync(8),
  syncWithWritesDuringRound(6),
  overlappingSyncs(4),
  goOffline(1),
  goOnline(6),
  burst(4);

  const _Action(this.weight);

  final int weight;
}

typedef _WritesDraw = ({
  bool isChosen,
  int count,
  List<({double actionRoll, double targetRoll})> writes,
});

/// Every step draws the same amount of randomness regardless of the state, so
/// a shorter run of the same seed replays a prefix of the longer schedule.
typedef _StepDraw = ({
  int deviceIndex,
  double actionRoll,
  double targetRoll,
  double variantRoll,
  double userRoll,
  List<int> burstOrder,
  int burstSize,
  bool isBurstWithMembership,
  List<double> burstFaultRolls,
  double secondFaultRoll,
  _WritesDraw writes,
  List<_WritesDraw> burstWrites,
});

class _CheckFailed implements Exception {
  const _CheckFailed(this.violation);

  final CheckViolation violation;
}

class SimulationRun {
  SimulationRun({
    required this.configuration,
    required this.mode,
    required this.seed,
  });

  static const _maximumFinishPasses = 5;
  static const _localWrites = [
    _Action.ensureList,
    _Action.addItem,
    _Action.renameItem,
    _Action.toggleChecked,
    _Action.deleteItem,
  ];
  static const _faultsThatFailTheRound = {
    SyncFault.serverUnavailable,
    SyncFault.failedAfterProcessing,
    SyncFault.truncatedResponse,
  };
  static final _startTime = DateTime.utc(2026, 6, 1, 12);
  static const _clockSkews = [
    Duration.zero,
    Duration(seconds: 4),
    Duration(seconds: -4),
    Duration(minutes: 3),
    Duration(minutes: -3),
  ];

  final SimulationConfiguration configuration;
  final SimulationMode mode;
  final int seed;
  final _ledger = ServerLedger();
  final _stepLog = <String>[];
  final _discardedRounds = <String>[];
  final _devices = <SimulatedDevice>[];
  final _users = <SimulatedUser>[];
  // Listed once per fault (and at least once) so that at least half of the
  // rounds run without a fault.
  late final _faultChoices = [
    ...List.filled(max(configuration.faults.length, 1), SyncFault.none),
    ...configuration.faults,
  ];
  var _step = 0;

  /// Returns the failure message, or null if every check passed.
  Future<String?> run() async {
    final random = Random(seed);
    try {
      await _createUsersAndDevices(random);
      for (final device in _devices) {
        _stepLog.add('step 0 ${await _sync(device, [SyncFault.none])}');
      }
      for (_step = 1; _step <= configuration.stepCount; _step++) {
        _stepLog.add('step $_step ${await _runStep(_drawStep(random))}');
      }
      if (!await _finish()) {
        return _failure([
          (
            check: 'finish',
            details: [
              'the devices still had pending commands or changes after '
                  '$_maximumFinishPasses fault-free passes',
            ],
          ),
        ]);
      }
      final violations = ConvergenceChecks(
        ledger: _ledger,
        users: _users,
        deviceTables: {
          for (final device in _devices) device: await device.readTables(),
        },
        referenceTables: await _readReferenceTables(),
        discardedRounds: _discardedRounds,
      ).run(includesLastWriterWins: mode == SimulationMode.sequential);
      return violations.isEmpty ? null : _failure(violations);
    } on _CheckFailed catch (failure) {
      return _failure([failure.violation]);
    } on Object catch (error, stackTrace) {
      return _failure([
        (check: 'unexpected error', details: ['$error\n$stackTrace']),
      ]);
    } finally {
      for (final device in _devices) {
        await device.close();
      }
    }
  }

  Future<void> _createUsersAndDevices(Random random) async {
    final client = Dio(BaseOptions(baseUrl: configuration.apiUrl));
    try {
      for (final name in ['A', 'B', 'C']) {
        final subject = 'sync-simulation-${mode.name}-seed-$seed-user-$name';
        final response = await client.get<Map<String, Object?>>(
          '/v1/users/me',
          options: Options(
            headers: {
              'Authorization':
                  'Bearer ${createJsonWebToken(configuration, subject)}',
            },
          ),
        );
        _users.add(
          SimulatedUser(
            name: name,
            subject: subject,
            id: response.data!['id']! as String,
          ),
        );
      }
    } finally {
      client.close();
    }
    final [userA, userB, userC] = _users;
    final clockSkews = [..._clockSkews]..shuffle(random);
    _devices.addAll([
      for (final (index, (name, user)) in [
        ('A1', userA),
        ('A2', userA),
        ('B1', userB),
        ('B2', userB),
        ('C1', userC),
      ].indexed)
        _createDevice(name, user, clockSkews[index]),
    ]);
  }

  SimulatedDevice _createDevice(
    String name,
    SimulatedUser user,
    Duration clockSkew,
  ) => SimulatedDevice(
    name: name,
    user: user,
    configuration: configuration,
    currentTime: () => _startTime.add(Duration(seconds: _step) + clockSkew),
    onSyncResponse: _ledger.recordSyncResponse,
  );

  _StepDraw _drawStep(Random random) => (
    deviceIndex: random.nextInt(_devices.length),
    actionRoll: random.nextDouble(),
    targetRoll: random.nextDouble(),
    variantRoll: random.nextDouble(),
    userRoll: random.nextDouble(),
    burstOrder: List.generate(_devices.length, (index) => index)
      ..shuffle(random),
    burstSize: 2 + random.nextInt(_devices.length - 1),
    isBurstWithMembership: random.nextBool(),
    burstFaultRolls: List.generate(_devices.length, (_) => random.nextDouble()),
    secondFaultRoll: random.nextDouble(),
    writes: _drawWrites(random),
    burstWrites: List.generate(_devices.length, (_) => _drawWrites(random)),
  );

  static _WritesDraw _drawWrites(Random random) => (
    isChosen: random.nextInt(3) == 0,
    count: 1 + random.nextInt(2),
    writes: List.generate(
      2,
      (_) => (actionRoll: random.nextDouble(), targetRoll: random.nextDouble()),
    ),
  );

  Future<String> _runStep(_StepDraw draw) async {
    final device = _devices[draw.deviceIndex];
    final tables = await device.readTables();
    final action = _pick(
      _weighted(
        _Action.values.where((action) => _isEnabled(action, device, tables)),
      ),
      draw.actionRoll,
    );
    final fault = _pick(_faultChoices, draw.variantRoll);
    return switch (action) {
      _Action.ensureList ||
      _Action.addItem ||
      _Action.renameItem ||
      _Action.toggleChecked ||
      _Action.deleteItem => _performWrite(
        device,
        action,
        tables,
        draw.targetRoll,
        's$_step',
      ),
      _Action.shareList => _share(
        device,
        _pick(tables.lists, draw.targetRoll),
        _pick(_otherUsersOf(device), draw.userRoll),
      ),
      _Action.leaveList => _leave(device, _pick(tables.lists, draw.targetRoll)),
      _Action.sync => _sync(device, [fault]),
      _Action.syncWithWritesDuringRound => _sync(device, [
        fault,
      ], writes: draw.writes),
      _Action.overlappingSyncs => _sync(device, [
        fault,
        _pick(_faultChoices, draw.secondFaultRoll),
      ], writes: draw.writes.isChosen ? draw.writes : null),
      _Action.goOffline => _setOffline(device, isOffline: true),
      _Action.goOnline => _setOffline(device, isOffline: false),
      _Action.burst => _burst(draw),
    };
  }

  bool _isEnabled(
    _Action action,
    SimulatedDevice device,
    DeviceTables tables,
  ) => switch (action) {
    _Action.ensureList => tables.lists.isEmpty,
    _Action.addItem => tables.lists.isNotEmpty,
    _Action.renameItem ||
    _Action.toggleChecked ||
    _Action.deleteItem => tables.items.isNotEmpty,
    _Action.shareList ||
    _Action.leaveList => !device.isOffline && tables.lists.isNotEmpty,
    _Action.goOffline => !device.isOffline,
    _Action.goOnline => device.isOffline,
    _Action.sync || _Action.syncWithWritesDuringRound => true,
    _Action.overlappingSyncs => mode == SimulationMode.overlapping,
    _Action.burst => mode != SimulationMode.sequential,
  };

  Future<String> _performWrite(
    SimulatedDevice device,
    _Action action,
    DeviceTables tables,
    double targetRoll,
    String nameSuffix,
  ) {
    final repository = device.repository;
    return _write(
      device,
      action.name,
      () => switch (action) {
        _Action.ensureList => repository.ensureList('list-$nameSuffix'),
        _Action.addItem => repository.addItem(
          _pick(tables.lists, targetRoll),
          'item-$nameSuffix',
        ),
        _Action.renameItem => repository.renameItem(
          _pick(tables.items, targetRoll),
          'renamed-$nameSuffix',
        ),
        _Action.toggleChecked => repository.toggleChecked(
          _pick(tables.items, targetRoll),
        ),
        _Action.deleteItem => repository.deleteItem(
          _pick(tables.items, targetRoll),
        ),
        _ => throw ArgumentError.value(action, 'action', 'Not a local write'),
      },
    );
  }

  Future<List<String>> _writeDuringRound(
    SimulatedDevice device,
    _WritesDraw draw,
  ) async {
    final descriptions = <String>[];
    for (final (index, (:actionRoll, :targetRoll))
        in draw.writes.take(draw.count).indexed) {
      final tables = await device.readTables();
      final action = _pick(
        _weighted(
          _localWrites.where((action) => _isEnabled(action, device, tables)),
        ),
        actionRoll,
      );
      descriptions.add(
        await _performWrite(
          device,
          action,
          tables,
          targetRoll,
          's$_step-${device.name}-w${index + 1}',
        ),
      );
    }
    return descriptions;
  }

  Future<String> _write(
    SimulatedDevice device,
    String action,
    Future<void> Function() write,
  ) async {
    await write();
    final enqueuedCommands = [
      for (final pending in (await device.readTables()).pendingCommands)
        if (!_ledger.commands.containsKey(pending.id))
          EnqueuedCommand(pending, device: device, step: _step),
    ];
    enqueuedCommands.forEach(_ledger.enqueue);
    final description = enqueuedCommands.isEmpty
        ? '$action without a command'
        : enqueuedCommands.map(_ledger.describe).join(', ');
    return '${_labelOf(device)} $description';
  }

  Future<String> _share(
    SimulatedDevice device,
    ShoppingList list,
    SimulatedUser user,
  ) async {
    final statusCode = await _statusCodeOf(
      () => device.repository.shareList(list, user.id),
    );
    if (statusCode == 204) {
      _ledger.recordMemberAdded(list.id, user);
    }
    return '${_labelOf(device)} shareList ${list.name} with user ${user.name}: '
        'HTTP $statusCode';
  }

  Future<String> _leave(SimulatedDevice device, ShoppingList list) async {
    final statusCode = await _statusCodeOf(
      () => device.repository.leaveList(list, device.user.id),
    );
    if (statusCode == 204) {
      _ledger.recordMemberRemoved(list.id, device.user);
    }
    return '${_labelOf(device)} leaveList ${list.name}: HTTP $statusCode';
  }

  Future<int> _statusCodeOf(Future<void> Function() request) async {
    try {
      await request();
      return 204;
    } on DioException catch (error) {
      if (error.response?.statusCode case final statusCode?) {
        return statusCode;
      }
      rethrow;
    }
  }

  /// Throws [_CheckFailed] if an outcome contradicts its fault or if the
  /// pending commands are not visible afterwards.
  Future<String> _sync(
    SimulatedDevice device,
    List<SyncFault> faults, {
    _WritesDraw? writes,
  }) async {
    final writeDescriptions = <String>[];
    final List<SyncAttempt> attempts;
    try {
      attempts = await device.sync(
        faults,
        duringRound: writes == null
            ? null
            : () async => writeDescriptions.addAll(
                await _writeDuringRound(device, writes),
              ),
      );
    } on Object catch (error, stackTrace) {
      throw _CheckFailed((
        check: 'round outcome',
        details: [
          'the sync of ${_labelOf(device)} in step $_step with the faults '
              '${faults.map((fault) => fault.name).join(', ')} threw '
              '$error\n$stackTrace',
        ],
      ));
    }
    final rounds = attempts.map(
      (attempt) =>
          'fault=${attempt.isOffline ? 'offline' : attempt.fault.name} '
          'outcome=${attempt.outcome.name} '
          '${_ledger.describeResponse(attempt.response)}',
    );
    final description =
        '${_labelOf(device)} '
        '${attempts.length == 1 ? 'sync' : 'overlapping syncs'} '
        '${rounds.join(' || ')}'
        '${writeDescriptions.isEmpty ? '' : '; written during the round: ${writeDescriptions.join(', ')}'}';
    for (final attempt in attempts) {
      if (attempt.outcome == SyncRoundOutcome.commandsDiscarded) {
        _discardedRounds.add('device ${device.name} in step $_step');
      }
      if (!attempt.isOffline &&
          _faultsThatFailTheRound.contains(attempt.fault) &&
          attempt.outcome != SyncRoundOutcome.failed) {
        throw _CheckFailed((
          check: 'round outcome',
          details: [
            'in step $_step the round with the fault ${attempt.fault.name} '
                'ended with ${attempt.outcome.name} instead of failed: '
                '$description',
          ],
        ));
      }
    }
    final replay = await device.replayPendingCommands();
    if (rowDifferenceOf(
          expected: replay.after,
          actual: replay.before,
          ledger: _ledger,
          users: _users,
        )
        case final difference?) {
      throw _CheckFailed((
        check: 'check 7 (pending commands visible)',
        details: [
          'device ${device.name} in step $_step after: $description',
          'its rows differ from a replay of its pending commands (- only '
              'after the replay, + only before it):\n$difference',
        ],
      ));
    }
    return description;
  }

  Future<String> _setOffline(
    SimulatedDevice device, {
    required bool isOffline,
  }) async {
    device.isOffline = isOffline;
    return '${_labelOf(device)} goes ${isOffline ? 'offline' : 'online'}';
  }

  Future<String> _burst(_StepDraw draw) async {
    final descriptions = await Future.wait<String?>([
      for (final (position, deviceIndex)
          in draw.burstOrder.take(draw.burstSize).indexed)
        _sync(
          _devices[deviceIndex],
          [_pick(_faultChoices, draw.burstFaultRolls[position])],
          writes: draw.burstWrites[position].isChosen
              ? draw.burstWrites[position]
              : null,
        ),
      _burstMembership(draw),
    ]);
    return 'burst: ${descriptions.nonNulls.join(' | ')}';
  }

  Future<String?> _burstMembership(_StepDraw draw) async {
    if (!draw.isBurstWithMembership || draw.burstSize == _devices.length) {
      return null;
    }
    final device = _devices[draw.burstOrder[draw.burstSize]];
    final lists = (await device.readTables()).lists;
    if (device.isOffline || lists.isEmpty) {
      return null;
    }
    final list = _pick(lists, draw.targetRoll);
    return draw.variantRoll < 0.75
        ? _share(device, list, _pick(_otherUsersOf(device), draw.userRoll))
        : _leave(device, list);
  }

  Future<bool> _finish() async {
    for (final device in _devices) {
      device.isOffline = false;
    }
    for (var pass = 1; pass <= _maximumFinishPasses; pass++) {
      final tablesBefore = [
        for (final device in _devices) await device.readTables(),
      ];
      for (final device in _devices) {
        _stepLog.add(
          'finish pass $pass ${await _sync(device, [SyncFault.none])}',
        );
      }
      final tablesAfter = [
        for (final device in _devices) await device.readTables(),
      ];
      if (tablesAfter.every((tables) => tables.pendingCommands.isEmpty) &&
          tablesBefore.indexed.every(
            (entry) => _areEqual(entry.$2, tablesAfter[entry.$1]),
          )) {
        return true;
      }
    }
    return false;
  }

  Future<Map<SimulatedUser, DeviceTables>> _readReferenceTables() async => {
    for (final user in _users) user: await _readReferenceTablesOf(user),
  };

  Future<DeviceTables> _readReferenceTablesOf(SimulatedUser user) async {
    final reference = _createDevice(
      '${user.name}-reference',
      user,
      Duration.zero,
    );
    try {
      _stepLog.add('reference ${await _sync(reference, [SyncFault.none])}');
      return await reference.readTables();
    } finally {
      await reference.close();
    }
  }

  List<SimulatedUser> _otherUsersOf(SimulatedDevice device) =>
      _users.where((user) => user != device.user).toList();

  String _labelOf(SimulatedDevice device) =>
      '${device.name} (user ${device.user.name})';

  String _failure(List<CheckViolation> violations) => [
    '${mode.name} simulation of seed $seed with ${configuration.stepCount} '
        'steps violated ${violations.map((violation) => violation.check).join(', ')}.',
    'Step log:',
    for (final line in _stepLog) '  $line',
    for (final (:check, :details) in violations) ...[
      'Violated $check:',
      for (final detail in details) '  $detail',
    ],
    'Reproduce this seed alone (in src/backend):',
    '  ${configuration.reproductionOf(seed)}',
  ].join('\n');

  static bool _areEqual(DeviceTables first, DeviceTables second) =>
      listEquals(first.lists, second.lists) &&
      listEquals(first.members, second.members) &&
      listEquals(first.items, second.items) &&
      listEquals(first.pendingCommands, second.pendingCommands);

  static List<_Action> _weighted(Iterable<_Action> actions) => [
    for (final action in actions)
      for (var count = 0; count < action.weight; count++) action,
  ];

  static T _pick<T>(List<T> candidates, double roll) =>
      candidates[(roll * candidates.length).floor()];
}
