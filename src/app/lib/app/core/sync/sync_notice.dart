import 'package:flutter/foundation.dart';

import 'sync_command_kind.dart';

enum SyncNoticeKind { rejectedCommand, parkedCommand, removedScope }

@immutable
class SyncNotice {
  const SyncNotice({
    required this.id,
    required this.kind,
    required this.scopeId,
    required this.dataType,
    required this.recordId,
    required this.commandKind,
    required this.reason,
    required this.label,
    required this.createdAt,
  });

  final int id;
  final SyncNoticeKind kind;
  final String scopeId;
  final String? dataType;
  final String? recordId;
  final SyncCommandKind? commandKind;
  final String? reason;
  final String? label;
  final DateTime createdAt;
}
