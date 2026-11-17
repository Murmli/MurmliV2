using Murmli.Backend.Aspire.Api;
using Murmli.Backend.Aspire.ApplicationInsights;
using Murmli.Backend.Aspire.Auth;
using Murmli.Backend.Aspire.ContainerAppEnvironment;
using Murmli.Backend.Aspire.Database;
using Murmli.Backend.Aspire.Identity;
using Murmli.Backend.Aspire.ProductImport;

var builder = DistributedApplication.CreateBuilder(args);

var environment = builder.AddContainerAppEnvironment();
var applicationInsights = builder.AddApplicationInsights(environment);
var identity = builder.AddBackendIdentity();
var database = builder.AddDatabase();
var betterAuthSecret = builder.AddBetterAuthSecret();
var authServiceKey = builder.AddAuthServiceKey();
var auth = builder.AddAuth(database, identity, applicationInsights, betterAuthSecret, authServiceKey);
builder.AddApi(database, identity, applicationInsights, auth, authServiceKey);
builder.AddProductImport(database, identity, applicationInsights);

builder.Build().Run();
