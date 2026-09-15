using Confera.Infrastructure.Persistence;
using Confera.MigrationWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddConferaPersistence(builder.Configuration, workerRetries: true);
builder.Services.AddSingleton<DemoInitializer>();
builder.Services.AddSingleton<MigrationService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MigrationService>());
using var host = builder.Build();
var migrationService = host.Services.GetRequiredService<MigrationService>();
await host.RunAsync();
return migrationService.ExitCode;
