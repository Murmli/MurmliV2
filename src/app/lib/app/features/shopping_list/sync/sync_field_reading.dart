/// Reads a field of a record from the server that must be present with a
/// value of type [T]. The message of the [FormatException] names only the
/// field, never its value.
T readRequiredField<T extends Object>(
  Map<String, Object?> fields,
  String field,
) => switch (fields[field]) {
  final T value => value,
  null => throw FormatException('$field is missing'),
  _ => throw FormatException('$field has the wrong type'),
};

T? readOptionalField<T extends Object>(
  Map<String, Object?> fields,
  String field,
) => switch (fields[field]) {
  final T value => value,
  null => null,
  _ => throw FormatException('$field has the wrong type'),
};
