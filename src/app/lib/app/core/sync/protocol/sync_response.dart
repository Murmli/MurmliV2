import 'package:dart_mappable/dart_mappable.dart';

import 'strict_json_type.dart';

part 'sync_response.mapper.dart';

/// The frame of a response. Every field is required, so a missing or
/// mistyped one makes the whole response unreadable. The settings are read
/// separately and leniently (`SyncServerSettings`).
@MappableClass()
class SyncResponse with SyncResponseMappable {
  const SyncResponse({
    required this.results,
    required this.scopes,
    required this.removedScopes,
    required this.coversAllScopes,
    required this.token,
    required this.hasMore,
    required this.serverTime,
  });

  final List<SyncCommandResult> results;
  final List<SyncScopeBlock> scopes;
  final List<SyncRemovedScope> removedScopes;
  @MappableField(hook: StrictJsonType<bool>())
  final bool coversAllScopes;
  @MappableField(hook: StrictJsonType<String>())
  final String token;
  @MappableField(hook: StrictJsonType<bool>())
  final bool hasMore;

  /// Unix milliseconds of the server clock when the response was complete.
  @MappableField(hook: StrictJsonType<int>())
  final int serverTime;
}

/// A closed set including the reserved `accepted`: an unknown status makes
/// the response unreadable.
@MappableEnum()
enum SyncResultStatus {
  applied,
  ignored,
  rejected,
  alreadyProcessed,
  failed,
  accepted,
}

@MappableEnum(defaultValue: SyncRejectionReason.invalid)
enum SyncRejectionReason {
  noAccess,
  limitExceeded,
  invalid,
  clockAhead,
  tooOld,
  accountMayNotWrite,
}

@MappableClass()
class SyncCommandResult with SyncCommandResultMappable {
  const SyncCommandResult({
    required this.commandId,
    required this.status,
    this.reason,
    this.original,
  });

  @MappableField(hook: StrictJsonType<String?>())
  final String? commandId;
  final SyncResultStatus status;
  final SyncRejectionReason? reason;
  final SyncOriginalResult? original;
}

@MappableClass()
class SyncOriginalResult with SyncOriginalResultMappable {
  const SyncOriginalResult({required this.status, this.reason});

  final SyncResultStatus status;
  final SyncRejectionReason? reason;
}

@MappableClass()
class SyncScopeBlock with SyncScopeBlockMappable {
  const SyncScopeBlock({
    required this.scopeId,
    required this.kind,
    required this.fullState,
    required this.records,
    required this.deletedRecords,
    required this.members,
  });

  @MappableField(hook: StrictJsonType<String>())
  final String scopeId;
  @MappableField(hook: StrictJsonType<String>())
  final String kind;
  @MappableField(hook: StrictJsonType<bool>())
  final bool fullState;
  final List<SyncRecord> records;
  final List<SyncDeletedRecord> deletedRecords;
  final List<SyncMemberEntry> members;
}

/// [fields] stays untyped so that a single unreadable record is skipped
/// instead of making the whole response unreadable.
@MappableClass()
class SyncRecord with SyncRecordMappable {
  const SyncRecord({
    required this.dataType,
    required this.id,
    required this.fields,
  });

  @MappableField(hook: StrictJsonType<String>())
  final String dataType;
  @MappableField(hook: StrictJsonType<String>())
  final String id;
  final Object? fields;
}

@MappableClass()
class SyncDeletedRecord with SyncDeletedRecordMappable {
  const SyncDeletedRecord({required this.dataType, required this.id});

  @MappableField(hook: StrictJsonType<String>())
  final String dataType;
  @MappableField(hook: StrictJsonType<String>())
  final String id;
}

/// A closed set including the reserved `former`: an unknown state makes the
/// response unreadable.
@MappableEnum()
enum SyncMemberState { active, former }

/// [role] is kept as text, the device accepts roles it does not know.
@MappableClass()
class SyncMemberEntry with SyncMemberEntryMappable {
  const SyncMemberEntry({
    required this.memberId,
    required this.name,
    required this.role,
    required this.state,
    required this.isOwn,
  });

  @MappableField(hook: StrictJsonType<String>())
  final String memberId;
  @MappableField(hook: StrictJsonType<String>())
  final String name;
  @MappableField(hook: StrictJsonType<String>())
  final String role;
  final SyncMemberState state;
  @MappableField(hook: StrictJsonType<bool>())
  final bool isOwn;
}

@MappableClass()
class SyncRemovedScope with SyncRemovedScopeMappable {
  const SyncRemovedScope({
    required this.scopeId,
    required this.removedByOwnAccount,
  });

  @MappableField(hook: StrictJsonType<String>())
  final String scopeId;
  @MappableField(hook: StrictJsonType<bool>())
  final bool removedByOwnAccount;
}
