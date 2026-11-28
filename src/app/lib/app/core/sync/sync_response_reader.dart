import 'dart:convert';

import 'protocol/sync_problem_details.dart';
import 'protocol/sync_response.dart';
import 'protocol/sync_server_settings.dart';

typedef ReadSyncResponse = ({
  SyncResponse response,
  SyncServerSettings settings,
});

typedef ReadSyncProblem = ({
  SyncProblemDetails problem,
  SyncServerSettings settings,
});

/// Returns `null` for a response whose frame does not fit the commands sent.
ReadSyncResponse? readSyncResponse(String body, List<String> sentCommandIds) {
  final SyncResponse response;
  final SyncServerSettings settings;
  try {
    final json = jsonDecode(body) as Map<String, Object?>;
    settings = SyncServerSettings.read(json['settings']);
    response = SyncResponseMapper.fromMap(json);
  } on Object {
    return null;
  }
  final results = response.results;
  if (results.length > sentCommandIds.length) {
    return null;
  }
  for (final (index, result) in results.indexed) {
    if (result.commandId != sentCommandIds[index] ||
        (result.status == SyncResultStatus.alreadyProcessed &&
            !_isOriginalStatus(result.original?.status))) {
      return null;
    }
  }
  return (response: response, settings: settings);
}

/// The settings of a problem are those a 413 `request-too-large` carries.
ReadSyncProblem? readProblemDetails(String body) {
  try {
    final json = jsonDecode(body) as Map<String, Object?>;
    return (
      problem: SyncProblemDetailsMapper.fromMap(json),
      settings: SyncServerSettings.read(json),
    );
  } on Object {
    return null;
  }
}

bool _isOriginalStatus(SyncResultStatus? status) => switch (status) {
  null || SyncResultStatus.alreadyProcessed || SyncResultStatus.failed => false,
  _ => true,
};
