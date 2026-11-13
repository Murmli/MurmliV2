var builder = WebApplication.CreateBuilder(args);

builder.AddAzureNpgsqlDataSource("murmli");

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
