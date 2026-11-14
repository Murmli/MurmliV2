using System.IO.Compression;
using System.Text;

namespace Murmli.Backend.ProductImport.Tests.Infrastructure;

internal static class CompressedExport
{
    // Covers the 8 byte gzip trailer and the end of the compressed data, like an interrupted download
    private const int TruncatedByteCount = 20;

    public static byte[] Create(IEnumerable<string> lines, bool hasTrailingNewline = false)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(string.Join('\n', lines) + (hasTrailingNewline ? "\n" : string.Empty)));
        }

        return output.ToArray();
    }

    public static byte[] CreateTruncated(IEnumerable<string> lines) => Create(lines)[..^TruncatedByteCount];
}
