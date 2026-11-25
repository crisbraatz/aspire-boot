using Amazon.Runtime;
using Amazon.S3;

using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Images;
using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Persistence;

using JasperFx.CommandLine.Descriptions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AspireBoot.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers infrastructure adapters shared by the Api and the Worker.</summary>
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(TimeProvider.System);

        // The EF model (and its passkey table) is cached per process, so every host must map the same Identity schema.
        services.Configure<IdentityOptions>(options => options.Stores.SchemaVersion = PersistenceExtensions.IdentitySchemaVersion);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddTransient<IEmailSender, SmtpEmailSender>();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var storage = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var config = new AmazonS3Config { ServiceURL = storage.ServiceUrl, ForcePathStyle = true };
            return new AmazonS3Client(new BasicAWSCredentials(storage.AccessKey, storage.SecretKey), config);
        });
        services.AddSingleton<IAvatarStorage, S3AvatarStorage>();
        // Provisions the private bucket in `resources setup` and at host startup (AddResourceSetupOnStartup). JasperFx
        // only discovers resources through system parts, so the bucket is reported by StorageSystemPart.
        services.AddSingleton<StorageBucketResource>();
        services.AddSingleton<ISystemPart>(provider => new StorageSystemPart(provider.GetRequiredService<StorageBucketResource>()));

        services.AddOptions<AvatarOptions>()
            .Bind(configuration.GetSection(AvatarOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<AvatarImageProcessor>();
        services.AddSingleton<AvatarProcessingLimiter>();

        services.AddHybridCache();

        return services;
    }
}