using Thinktecture;

namespace Murmli.Backend.Data.Products;

[SmartEnum<string>]
public partial class ProductImportRunStatus
{
    public static readonly ProductImportRunStatus Running = new("running");

    public static readonly ProductImportRunStatus Succeeded = new("succeeded");

    public static readonly ProductImportRunStatus Failed = new("failed");
}
