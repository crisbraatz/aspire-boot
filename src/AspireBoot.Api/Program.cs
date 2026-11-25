using AspireBoot.Api.Shared;
using AspireBoot.Core;
using AspireBoot.Core.Persistence;

using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Resources;

using Microsoft.EntityFrameworkCore;

using Scalar.AspNetCore;

using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.HealthChecks;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>(
    AppDbContext.ConnectionName,
    settings => settings.DisableRetry = true, // Wolverine owns transactions; EF retries conflict with them.
    options => options.UseAspireBootNpgsql(builder.Configuration.GetConnectionString(AppDbContext.ConnectionName)));
builder.AddRedisDistributedCache("redis");

builder.Services.AddCoreInfrastructure(builder.Configuration);
builder.AddApiSecurity();

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddValidation();
builder.Services.AddOpenApi();

builder.Host.UseWolverine(options =>
{
    var connectionString = builder.Configuration.GetConnectionString(AppDbContext.ConnectionName)!;
    options.PersistMessagesWithPostgresql(connectionString, "wolverine_api");
    options.UseEntityFrameworkCoreTransactions();
    options.Policies.AutoApplyTransactions();
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
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
    .AddWolverine("wolverine", failureStatus: null, tags: ["live"], timeout: null);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development only. Production runs the migration bundle as an explicit deploy step.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapDefaultEndpoints();
app.MapApiEndpoints();

return await app.RunJasperFxCommands(args);

/// <summary>Entry point marker used by WebApplicationFactory in integration tests.</summary>
public partial class Program;