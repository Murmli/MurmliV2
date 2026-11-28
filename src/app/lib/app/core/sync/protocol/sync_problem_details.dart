import 'package:dart_mappable/dart_mappable.dart';

import 'strict_json_type.dart';

part 'sync_problem_details.mapper.dart';

/// Problem details of a failed request (RFC 9457). Reactions depend only on
/// [type], never on the status code alone.
@MappableClass()
class SyncProblemDetails with SyncProblemDetailsMappable {
  const SyncProblemDetails({this.type});

  @MappableField(hook: StrictJsonType<String?>())
  final String? type;
}
