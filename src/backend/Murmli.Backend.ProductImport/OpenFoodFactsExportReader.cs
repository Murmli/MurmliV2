using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport;

public sealed partial class OpenFoodFactsExportReader(
    HttpClient httpClient,
    TimeProvider timeProvider,
    ILogger<OpenFoodFactsExportReader> logger
)
{
    private const int LastKnownSchemaVersion = 1004;

    // HttpClient.Timeout only covers the response headers, a stalled body would block the read forever
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(2);

    private static readonly Uri ExportUri = new(
        "https://static.openfoodfacts.org/data/openfoodfacts-products.jsonl.gz"
    );

    // Also matches other tag arrays such as "origins_tags", the mapper checks "countries_tags" exactly
    private static ReadOnlySpan<byte> GermanyCountryTag => "\"en:germany\""u8;

    public async IAsyncEnumerable<Product> ReadGermanProductsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var response = await httpClient.GetAsync(
            ExportUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var export = new GZipStream(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            CompressionMode.Decompress
        );
        var pipeReader = PipeReader.Create(export, new StreamPipeReaderOptions(bufferSize: 1024 * 1024));
        using var inactivitySource = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
        using var readSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            inactivitySource.Token
        );
        var hasReadLine = false;
        var hasLoggedUnknownSchemaVersion = false;
        try
        {
            while (true)
            {
                var result = await ReadWithInactivityTimeoutAsync(
                    pipeReader,
                    inactivitySource,
                    readSource.Token,
                    cancellationToken
                );
                var buffer = result.Buffer;

                while (TryReadLine(ref buffer, result.IsCompleted, out var line))
                {
                    hasReadLine = true;
                    if (!ContainsGermanyCountryTag(line))
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(line);
                    if (
                        !hasLoggedUnknownSchemaVersion
                        && GetSchemaVersion(document.RootElement) is int schemaVersion
                        && schemaVersion > LastKnownSchemaVersion
                    )
                    {
                        LogUnknownSchemaVersion(schemaVersion);
                        hasLoggedUnknownSchemaVersion = true;
                    }

                    if (OpenFoodFactsProductMapper.Map(document.RootElement) is { } product)
                    {
                        yield return product;
                    }
                }

                pipeReader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Strict gzip validation accepts an empty body, the merge would then delete every product
            if (!hasReadLine)
            {
                throw new InvalidDataException("The Open Food Facts export contains no lines.");
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async ValueTask<ReadResult> ReadWithInactivityTimeoutAsync(
        PipeReader pipeReader,
        CancellationTokenSource inactivitySource,
        CancellationToken readCancellationToken,
        CancellationToken cancellationToken
    )
    {
        inactivitySource.CancelAfter(InactivityTimeout);
        try
        {
            return await pipeReader.ReadAsync(readCancellationToken);
        }
        catch (OperationCanceledException exception)
            when (inactivitySource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Open Food Facts export sent no data for {InactivityTimeout.TotalMinutes} minutes.",
                exception
            );
        }
        finally
        {
            inactivitySource.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    private static bool TryReadLine(
        ref ReadOnlySequence<byte> buffer,
        bool isEndOfExport,
        out ReadOnlySequence<byte> line
    )
    {
        var reader = new SequenceReader<byte>(buffer);
        if (reader.TryReadTo(out line, (byte)'\n'))
        {
            buffer = buffer.Slice(reader.Position);
            return true;
        }

        if (isEndOfExport && !buffer.IsEmpty)
        {
            line = buffer;
            buffer = buffer.Slice(buffer.End);
            return true;
        }

        return false;
    }

    private static bool ContainsGermanyCountryTag(ReadOnlySequence<byte> line)
    {
        if (line.IsSingleSegment)
        {
            return line.FirstSpan.IndexOf(GermanyCountryTag) >= 0;
        }

        var length = checked((int)line.Length);
        var lineBytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            line.CopyTo(lineBytes);
            return lineBytes.AsSpan(0, length).IndexOf(GermanyCountryTag) >= 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lineBytes);
        }
    }

    private static int? GetSchemaVersion(JsonElement product) =>
        product.ValueKind == JsonValueKind.Object
        && product.TryGetProperty("schema_version", out var schemaVersion)
        && schemaVersion.ValueKind == JsonValueKind.Number
        && schemaVersion.TryGetInt32(out var version)
            ? version
            : null;
}
