import 'package:uuid/data.dart';
import 'package:uuid/rng.dart';
import 'package:uuid/uuid.dart';

String newUuid() => const Uuid().v4(config: V4Options(null, CryptoRNG()));
