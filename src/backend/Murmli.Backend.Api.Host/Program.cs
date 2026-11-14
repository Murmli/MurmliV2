using Murmli.Backend.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddApi();

var app = builder.Build();

app.MapApi();

app.Run();
