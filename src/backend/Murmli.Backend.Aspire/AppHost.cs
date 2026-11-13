using Azure.Provisioning.Expressions;
using Azure.Provisioning.PostgreSql;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("env").WithDashboard(false);

var database = builder
    .AddAzurePostgresFlexibleServer("postgres")
    .ConfigureInfrastructure(infrastructure =>
    {
        var server = infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
        // Azure.Provisioning.PostgreSql 1.1.1 has no enum member for version 18
        server.Version = new StringLiteralExpression("18");
    })
    .RunAsContainer(postgres => postgres.WithImageTag("18"))
    .AddDatabase("murmli");

builder
    .AddProject<Projects.Murmli_Backend_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .PublishAsAzureContainerApp(
        (_, containerApp) =>
        {
            containerApp.Template.Scale.MinReplicas = 0;
            containerApp.Template.Scale.MaxReplicas = 1;
            var container = containerApp.Template.Containers.Single().Value!;
            container.Resources.Cpu = 0.25;
            container.Resources.Memory = "0.5Gi";
        }
    );

builder.Build().Run();
