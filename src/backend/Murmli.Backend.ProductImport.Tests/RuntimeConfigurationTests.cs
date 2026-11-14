using System.Text.Json.Nodes;

namespace Murmli.Backend.ProductImport.Tests;

public class RuntimeConfigurationTests
{
    // RuntimeHostConfigurationOption only applies to the starting host, so the truncation test covers the test host's copy
    [Fact]
    public void RuntimeConfiguration_ProductImportHost_EnablesStrictGzipValidation()
    {
        var runtimeConfiguration = JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Murmli.Backend.ProductImport.Host.runtimeconfig.json"))
        );

        Assert.True(
            runtimeConfiguration?["runtimeOptions"]?["configProperties"]?["System.IO.Compression.UseStrictValidation"]
                ?.GetValue<bool>()
        );
    }
}
