import 'package:dart_mappable/dart_mappable.dart';

part 'sync_request.mapper.dart';

@MappableClass(ignoreNull: true)
class SyncRequest with SyncRequestMappable {
  const SyncRequest({
    required this.token,
    required this.dataTypes,
    required this.commands,
    required this.pendingCommandCount,
    required this.oldestPendingCommandAgeSeconds,
  });

  final String? token;
  final List<String> dataTypes;
  final List<SyncRequestCommand> commands;
  final int? pendingCommandCount;
  final int? oldestPendingCommandAgeSeconds;
}

@MappableClass(ignoreNull: true)
class SyncRequestCommand with SyncRequestCommandMappable {
  const SyncRequestCommand({
    required this.id,
    required this.kind,
    required this.scopeId,
    required this.dataType,
    required this.recordId,
    required this.time,
    required this.fields,
  });

  final String id;
  final String kind;
  final String scopeId;
  final String dataType;
  final String recordId;

  /// Unix milliseconds after the corrected clock of the device.
  final int time;

  final Map<String, Object?>? fields;
}
