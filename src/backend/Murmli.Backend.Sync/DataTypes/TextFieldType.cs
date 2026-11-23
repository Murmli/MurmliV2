using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class TextFieldType(int minimumLength, int maximumLength, Regex? pattern) : SyncFieldType<string>
{
    internal override string DefaultOnCreate => "";

    internal override bool IsNullable => false;

    internal override bool HasDefaultOnCreate => minimumLength == 0 && MatchesPattern("");

    internal override bool TryRead(JsonElement element, [MaybeNullWhen(false)] out string value) =>
        element.TryGetReadableString(out value) && value.Length >= minimumLength && value.Length <= maximumLength && MatchesPattern(value);

    internal override void Write(Utf8JsonWriter writer, string value) => writer.WriteStringValue(value);

    private bool MatchesPattern(string value) => pattern is null || pattern.IsMatch(value);
}
