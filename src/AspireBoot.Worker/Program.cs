using AspireBoot.Core;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;
using AspireBoot.Worker.Identity.Notifications.SendConfirmationEmail;
using AspireBoot.Worker.Identity.Sessions.CleanupUserSessions;

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

// Must precede AddNpgsqlDbContext: the audit interceptor is added through ConfigureDbContext, which the pooled
// context options read when they are first built.
builder.Services.AddCoreInfrastructure(builder.Configuration);

builder.AddNpgsqlDbContext<AppDbContext>(
    AppDbContext.ConnectionName,
    settings => settings.DisableRetry = true,
    options => options.UseAspireBootNpgsql(builder.Configuration.GetConnectionString(AppDbContext.ConnectionName)));
builder.AddRedisDistributedCache("redis");

builder.Services.AddOptions<UserSessionsCleanupOptions>()
    .Bind(builder.Configuration.GetSection(UserSessionsCleanupOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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
    options.ListenToRabbitQueue("identity-notifications");
    options.ListenToRabbitQueue("identity-avatars");
    options.Discovery.IncludeAssembly(typeof(SendConfirmationEmailHandler).Assembly);

    // Daily at 03:17 UTC on one node; a run still in progress is skipped, and a missed or failed run is caught up by the next one.
    options.Schedules.ScheduleRecurring<CleanupUserSessions>("17 3 * * *", TimeZoneInfo.Utc).SkipWhileRunning();

    options.Services.CritterStackDefaults(defaults =>
    {
        defaults.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;
        defaults.Development.ResourceAutoCreate = AutoCreate.CreateOrUpdate;
        // Tests host the apps with the "Testing" environment and need runtime handler discovery; production stays pre-generated.
        defaults.Production.GeneratedCodeMode = builder.Environment.IsEnvironment("Testing") ? TypeLoadMode.Dynamic : TypeLoadMode.Static;
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

namespace AspireBoot.Worker
{
    /// <summary>Marker type so integration tests can host the Worker with WebApplicationFactory.</summary>
    public partial class Program;
}