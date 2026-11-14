namespace Murmli.Backend.Data.Products;

public class ProductImportRun
{
    public long Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public required ProductImportRunStatus Status { get; set; }

    public long? PeakMemoryBytes { get; set; }
}
