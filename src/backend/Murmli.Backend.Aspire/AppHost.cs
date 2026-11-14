using Murmli.Backend.Aspire.Api;
using Murmli.Backend.Aspire.ContainerAppEnvironment;
using Murmli.Backend.Aspire.Database;
using Murmli.Backend.Aspire.Identity;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddContainerAppEnvironment();
var identity = builder.AddBackendIdentity();
var database = builder.AddDatabase();
builder.AddApi(database, identity);

builder.Build().Run();
