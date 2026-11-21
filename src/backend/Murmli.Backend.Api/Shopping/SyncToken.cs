using System.Buffers.Text;
using System.Text.Json;

namespace Murmli.Backend.Api.Shopping;

internal static class SyncToken
{
    public static string Encode(IReadOnlyDictionary<Guid, long> listVersions) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(listVersions, JsonSerializerOptions.Web));

    public static Dictionary<Guid, long> Decode(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, long>>(Base64Url.DecodeFromChars(token), JsonSerializerOptions.Web) ?? [];
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return [];
        }
    }
}
