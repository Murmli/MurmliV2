using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.ProductImport;

public class ExtensionsTests
{
    [Fact]
    public async Task AddProductImport_ProductLimitConfigured_PassesLimitToWorker()
    {
        var environment = await AppHostEnvironment.GetAsync("product-import", ["--ProductImport:ProductLimit=100"]);

        Assert.Equal("100", environment["ProductImport__ProductLimit"]);
    }

    [Fact]
    public async Task AddProductImport_NoProductLimit_PassesNoLimitToWorker()
    {
        var environment = await AppHostEnvironment.GetAsync("product-import", []);

        Assert.DoesNotContain("ProductImport__ProductLimit", environment.Keys);
    }
}
