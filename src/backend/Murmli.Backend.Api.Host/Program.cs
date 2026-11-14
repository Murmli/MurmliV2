using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Telemetry.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();
builder.AddApi();

var app = builder.Build();

app.MapApi();

app.Run();
