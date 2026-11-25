using Aspire.Hosting.Dotnet;
// Local development resource graph. Production uses deploy/compose.yaml.
// Container images are pinned to the same tags as deploy/compose.yaml.
var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var rabbitUser = builder.AddParameter("rabbitmq-user", "aspireboot");
var rabbitPassword = builder.AddParameter("rabbitmq-password", secret: true);
var storageAccessKey = builder.AddParameter("storage-access-key", "aspireboot-dev");
var storageSecretKey = builder.AddParameter("storage-secret-key", secret: true);

var database = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImage("library/postgres", "19.0-alpine")
    .WithDataVolume()
    .AddDatabase("aspireboot");

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitUser, rabbitPassword)
    .WithImage("library/rabbitmq", "4.4.1-management-alpine")
    .WithManagementPlugin()
    .WithDataVolume();

var redis = builder.AddRedis("redis")
    .WithImage("library/redis", "8.12.0-alpine")
    .WithDataVolume();

var storage = builder.AddContainer("rustfs", "rustfs/rustfs", "1.0.6")
    .WithEnvironment("RUSTFS_ACCESS_KEY", storageAccessKey)
    .WithEnvironment("RUSTFS_SECRET_KEY", storageSecretKey)
    .WithHttpEndpoint(targetPort: 9000, name: "s3")
    .WithHttpEndpoint(targetPort: 9001, name: "console")
    .WithVolume("aspireboot-rustfs-data", "/data");

var mailpit = builder.AddMailPit("mailpit");
var smtp = mailpit.GetEndpoint("smtp");

void WireShared(IResourceBuilder<DotnetProjectResource> project) => project
    .WithReference(database).WaitFor(database)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(redis).WaitFor(redis)
    .WaitFor(storage)
    .WithEnvironment("Storage__ServiceUrl", storage.GetEndpoint("s3"))
    .WithEnvironment("Storage__AccessKey", storageAccessKey)
    .WithEnvironment("Storage__SecretKey", storageSecretKey)
    .WaitFor(mailpit)
    .WithEnvironment("Email__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Port", smtp.Property(EndpointProperty.Port));

var api = builder.AddDotnetProject("api", "../AspireBoot.Api/AspireBoot.Api.csproj");
WireShared(api);

var worker = builder.AddDotnetProject("worker", "../AspireBoot.Worker/AspireBoot.Worker.csproj");
WireShared(worker);
worker.WaitFor(api); // The API applies development migrations first.

var web = builder.AddJavaScriptApp("web", "../AspireBoot.Web", "start")
    .WithHttpEndpoint(env: "PORT")
    .WaitFor(api);

// Caddy fronts the SPA and the API on one HTTPS origin, exactly like production.
builder.AddContainer("caddy", "library/caddy", "2.11.9-alpine")
    .WithBindMount("Caddyfile.dev", "/etc/caddy/Caddyfile", isReadOnly: true)
    .WithVolume("aspireboot-caddy-data", "/data")
    .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
    .WithEnvironment("WEB_UPSTREAM", web.GetEndpoint("http"))
    .WithHttpsEndpoint(port: 8443, targetPort: 8443, name: "https")
    .WithExternalHttpEndpoints()
    .WaitFor(api)
    .WaitFor(web);

builder.Build().Run();