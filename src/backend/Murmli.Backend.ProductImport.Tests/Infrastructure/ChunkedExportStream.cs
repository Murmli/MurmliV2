namespace Murmli.Backend.ProductImport.Tests.Infrastructure;

internal sealed class ChunkedExportStream(
    byte[] export,
    int chunkCount,
    Func<int, CancellationToken, Task> onChunkRequested
) : Stream
{
    private readonly int chunkSize = (export.Length + chunkCount - 1) / chunkCount;

    private int position;

    private int chunkIndex;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (position == export.Length)
        {
            return 0;
        }

        await onChunkRequested(chunkIndex++, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var chunk = export.AsMemory(position, Math.Min(Math.Min(chunkSize, buffer.Length), export.Length - position));
        chunk.CopyTo(buffer);
        position += chunk.Length;
        return chunk.Length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
