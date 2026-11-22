using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Murmli.Backend.Sync.Extensions;

internal static class JsonPropertyExtensions
{
    public static bool TryGetReadableName(this JsonProperty property, [NotNullWhen(true)] out string? name)
    {
        try
        {
            name = property.Name;
            return true;
        }
        catch (InvalidOperationException)
        {
            // A lone surrogate escape cannot be read
            name = null;
            return false;
        }
    }
}
