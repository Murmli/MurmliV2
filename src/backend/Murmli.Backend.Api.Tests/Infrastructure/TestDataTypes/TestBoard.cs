using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestBoard : SyncRecord
{
    public string Title { get; set; } = "";

    public DateTimeOffset TitleChangedAt { get; set; }
}
