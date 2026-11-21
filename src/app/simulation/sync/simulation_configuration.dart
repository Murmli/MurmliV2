import 'dart:convert';

import 'fault_injecting_http_client_adapter.dart';

const _launchCommand =
    'dotnet test --project Murmli.Backend.Api.Tests --explicit only '
    '--filter-class "*ShoppingSyncSimulationTests" --show-live-output on';

class SimulationConfiguration {
  SimulationConfiguration.fromEnvironment(Map<String, String> environment)
    : apiUrl = _required(environment, 'SYNC_SIMULATION_API_URL'),
      tokenSigningKey = base64Decode(
        _required(environment, 'SYNC_SIMULATION_TOKEN_SIGNING_KEY'),
      ),
      tokenIssuer = _required(environment, 'SYNC_SIMULATION_TOKEN_ISSUER'),
      tokenAudience = _required(environment, 'SYNC_SIMULATION_TOKEN_AUDIENCE'),
      firstSeed = _integer(environment, 'SYNC_SIMULATION_FIRST_SEED') ?? 1,
      seedCount = _positiveInteger(
        environment,
        'SYNC_SIMULATION_SEED_COUNT',
        100,
      ),
      stepCount = _positiveInteger(
        environment,
        'SYNC_SIMULATION_STEP_COUNT',
        400,
      ),
      faults = _faults(environment['SYNC_SIMULATION_FAULTS']);

  final String apiUrl;
  final List<int> tokenSigningKey;
  final String tokenIssuer;
  final String tokenAudience;
  final int firstSeed;
  final int seedCount;
  final int stepCount;
  final List<SyncFault> faults;

  Iterable<int> get seeds =>
      Iterable.generate(seedCount, (index) => firstSeed + index);

  String reproductionOf(int seed) => [
    'SYNC_SIMULATION_FIRST_SEED=$seed',
    'SYNC_SIMULATION_SEED_COUNT=1',
    'SYNC_SIMULATION_STEP_COUNT=$stepCount',
    'SYNC_SIMULATION_FAULTS=${faults.map((fault) => fault.name).join(',')}',
    _launchCommand,
  ].join(' ');

  static String _required(Map<String, String> environment, String name) =>
      environment[name] ??
      (throw StateError(
        '$name is not set. The simulation is started through the explicit '
        'test ShoppingSyncSimulationTests, in src/backend run: $_launchCommand',
      ));

  static int? _integer(Map<String, String> environment, String name) {
    final value = environment[name];
    if (value == null) {
      return null;
    }
    return int.tryParse(value) ??
        (throw ArgumentError.value(value, name, 'Not an integer'));
  }

  static int _positiveInteger(
    Map<String, String> environment,
    String name,
    int defaultValue,
  ) {
    final value = _integer(environment, name) ?? defaultValue;
    if (value < 1) {
      throw ArgumentError.value(value, name, 'Must be at least 1');
    }
    return value;
  }

  static const _allowedFaults = [
    SyncFault.requestLost,
    SyncFault.responseLost,
    SyncFault.serverUnavailable,
    SyncFault.failedAfterProcessing,
    SyncFault.truncatedResponse,
  ];

  static List<SyncFault> _faults(String? names) {
    if (names == null) {
      return _allowedFaults;
    }
    return {
      for (final name in names.split(',').map((name) => name.trim()))
        if (name.isNotEmpty)
          _allowedFaults.firstWhere(
            (fault) => fault.name == name,
            orElse: () => throw ArgumentError.value(
              names,
              'SYNC_SIMULATION_FAULTS',
              'Allowed are '
                  '${_allowedFaults.map((fault) => fault.name).join(', ')}',
            ),
          ),
    }.toList();
  }
}
