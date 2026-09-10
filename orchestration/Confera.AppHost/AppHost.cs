var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Confera_Api>("confera-api");

builder.Build().Run();
