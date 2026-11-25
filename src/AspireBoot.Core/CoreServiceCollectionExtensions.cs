using Amazon.Runtime;
using Amazon.S3;

using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Storage;

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

        services.AddHybridCache();

        return services;
    }
}