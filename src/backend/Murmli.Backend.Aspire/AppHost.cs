using Murmli.Backend.Aspire.Api;
using Murmli.Backend.Aspire.ApplicationInsights;
using Murmli.Backend.Aspire.ContainerAppEnvironment;
using Murmli.Backend.Aspire.Database;
using Murmli.Backend.Aspire.Identity;
using Murmli.Backend.Aspire.ProductImport;

var builder = DistributedApplication.CreateBuilder(args);

var environment = builder.AddContainerAppEnvironment();
var applicationInsights = builder.AddApplicationInsights(environment);
var identity = builder.AddBackendIdentity();
var database = builder.AddDatabase();
builder.AddApi(database, identity, applicationInsights);
builder.AddProductImport(database, identity, applicationInsights);

builder.Build().Run();
