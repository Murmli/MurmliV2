using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Telemetry.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();
builder.AddApi();

await using var app = builder.Build();

await app.MigrateDatabaseAsync();

app.UseApi();

await app.RunAsync();
