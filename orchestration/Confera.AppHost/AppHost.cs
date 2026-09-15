using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var isolated = builder.Configuration.GetValue<bool>("Persistence:Isolated");
var postgres = builder.AddPostgres("confera-postgres").WithImageTag("18.6");
if (!isolated)
{
    postgres.WithVolume("confera-postgres-dev", "/var/lib/postgresql");
}

var database = postgres.AddDatabase("confera");
var worker = builder.AddProject<Projects.Confera_MigrationWorker>("confera-migrations")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("DemoSeed__Enabled", "true");

builder.AddProject<Projects.Confera_Api>("confera-api")
    .WithReference(database).WaitForCompletion(worker)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
