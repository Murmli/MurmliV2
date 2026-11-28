import 'package:dart_mappable/dart_mappable.dart';

/// dart_mappable converts mismatching primitives silently, such as `'yes'` to
/// `false` or `42` to `'42'`. This hook rejects a value that is not a [T].
class StrictJsonType<T> extends MappingHook {
  const StrictJsonType();

  @override
  Object? beforeDecode(Object? value) => value is T
      ? value
      : throw FormatException('Expected $T, found ${value.runtimeType}');
}
