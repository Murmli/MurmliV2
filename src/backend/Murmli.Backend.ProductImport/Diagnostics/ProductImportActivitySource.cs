using System.Diagnostics;

namespace Murmli.Backend.ProductImport.Diagnostics;

internal static class ProductImportActivitySource
{
    public static readonly ActivitySource Instance = new("Murmli.Backend.ProductImport");
}
