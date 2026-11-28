import 'package:riverpod_annotation/riverpod_annotation.dart';

import 'sync_data_type.dart';

part 'sync_data_types_provider.g.dart';

@Riverpod(keepAlive: true)
List<SyncDataType> syncDataTypes(Ref ref) => const [];
