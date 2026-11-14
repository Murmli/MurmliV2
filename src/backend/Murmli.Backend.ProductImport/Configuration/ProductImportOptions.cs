using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.ProductImport.Configuration;

public sealed class ProductImportOptions
{
    public const string SectionName = "ProductImport";

    [Range(1, int.MaxValue)]
    public int? ProductLimit { get; set; }
}
