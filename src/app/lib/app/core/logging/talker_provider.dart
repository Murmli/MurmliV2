import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:talker/talker.dart';

part 'talker_provider.g.dart';

@Riverpod(keepAlive: true)
Talker talker(Ref ref) =>
    throw UnimplementedError('talkerProvider must be overridden');
