namespace Murmli.Backend.Data.Shopping;

public class ProcessedCommand
{
    public required Guid Id { get; set; }

    public required Guid UserId { get; set; }

    public required DateTimeOffset ProcessedAt { get; set; }
}
