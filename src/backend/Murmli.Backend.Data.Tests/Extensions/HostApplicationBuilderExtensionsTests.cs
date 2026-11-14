using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Data.Tests.Extensions;

public class HostApplicationBuilderExtensionsTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void AddMurmliDbContext_SensitiveDataSwitch_ConfiguresSensitiveDataLogging(string? recordSensitiveData, bool expectedEnabled)
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Production }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // A password keeps Aspire from requesting an Entra token
                ["ConnectionStrings:murmli"] = "Host=localhost;Database=murmli;Username=postgres;Password=postgres",
                ["Telemetry:RecordSensitiveData"] = recordSensitiveData,
            }
        );

        builder.AddMurmliDbContext();
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MurmliDbContext>();

        Assert.Equal(
            expectedEnabled,
            context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled
        );
    }
}
