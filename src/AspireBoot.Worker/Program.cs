using AspireBoot.Core;
using AspireBoot.Core.Persistence;

using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Resources;

using Microsoft.AspNetCore.DataProtection;

using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.HealthChecks;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

// The Worker consumes integration messages (durable inbox) and runs scheduled jobs.
// It exposes only /health and /alive on the internal network.
var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>(
    AppDbContext.ConnectionName,
    settings => settings.DisableRetry = true,
    options => options.UseAspireBootNpgsql(builder.Configuration.GetConnectionString(AppDbContext.ConnectionName)));
builder.AddRedisDistributedCache("redis");

builder.Services.AddCoreInfrastructure(builder.Configuration);
builder.Services.AddDataProtection()
    .SetApplicationName("AspireBoot")
    .PersistKeysToDbContext<AppDbContext>();

builder.Host.UseWolverine(options =>
{
    var connectionString = builder.Configuration.GetConnectionString(AppDbContext.ConnectionName)!;
    options.PersistMessagesWithPostgresql(connectionString, "wolverine_worker");
    options.UseEntityFrameworkCoreTransactions();
    options.Policies.AutoApplyTransactions();
    options.Policies.UseDurableInboxOnAllListeners();
    options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    options.UseRabbitMqUsingNamedConnection("rabbitmq").AutoProvision();

    options.Services.CritterStackDefaults(defaults =>
    {
        defaults.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;
        defaults.Development.ResourceAutoCreate = AutoCreate.CreateOrUpdate;
        defaults.Production.GeneratedCodeMode = TypeLoadMode.Static;
        defaults.Production.ResourceAutoCreate = AutoCreate.None;
    });
});
builder.Services.AddResourceSetupOnStartup();
builder.Services.AddHealthChecks()
    .AddWolverine("wolverine", failureStatus: null, tags: ["live"], timeout: null)
    .AddWolverineListeners("wolverine-listeners", filter: null, failureStatus: null, tags: ["ready"], timeout: null);

var app = builder.Build();

app.MapDefaultEndpoints();

return await app.RunJasperFxCommands(args);