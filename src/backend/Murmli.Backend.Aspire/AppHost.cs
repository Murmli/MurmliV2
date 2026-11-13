using Murmli.Backend.Aspire.Api;
using Murmli.Backend.Aspire.ContainerAppEnvironment;
using Murmli.Backend.Aspire.Database;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddContainerAppEnvironment();
var database = builder.AddDatabase();
builder.AddApi(database);

builder.Build().Run();
