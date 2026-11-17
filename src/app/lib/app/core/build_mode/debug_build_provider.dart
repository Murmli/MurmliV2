import 'package:flutter/foundation.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'debug_build_provider.g.dart';

@Riverpod(keepAlive: true)
bool debugBuild(Ref ref) => kDebugMode;
