using Murmli.Backend.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddApi();

var app = builder.Build();

app.MapApi();

app.Run();
