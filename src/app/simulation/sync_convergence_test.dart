import 'dart:io';

import 'package:drift/drift.dart';
import 'package:flutter_test/flutter_test.dart';

import 'sync/simulation_configuration.dart';
import 'sync/simulation_run.dart';

void main() {
  // Every simulated device owns a separate in-memory database.
  driftRuntimeOptions.dontWarnAboutMultipleDatabases = true;
  final configuration = SimulationConfiguration.fromEnvironment(
    Platform.environment,
  );

  test(
    'sync of five devices acting one at a time converges and follows the server verdicts',
    () => _runSeeds(configuration, SimulationMode.sequential),
    timeout: Timeout.none,
  );

  test(
    'sync of five devices with concurrent rounds converges to the server state',
    () => _runSeeds(configuration, SimulationMode.parallel),
    timeout: Timeout.none,
  );

  test(
    'sync of five devices with overlapping rounds of one device converges to the server state',
    () => _runSeeds(configuration, SimulationMode.overlapping),
    timeout: Timeout.none,
  );
}

Future<void> _runSeeds(
  SimulationConfiguration configuration,
  SimulationMode mode,
) async {
  for (final seed in configuration.seeds) {
    final failure = await SimulationRun(
      configuration: configuration,
      mode: mode,
      seed: seed,
    ).run();
    if (failure != null) {
      fail(failure);
    }
  }
}
