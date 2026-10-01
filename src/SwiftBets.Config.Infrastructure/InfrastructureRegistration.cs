using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Config.Application.Ports;
using SwiftBets.Config.Infrastructure.Persistence;

namespace SwiftBets.Config.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddConfigInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SbConfig")
            ?? throw new InvalidOperationException("ConnectionStrings:SbConfig is required.");
        services.AddPostgresPersistence(connectionString);
        services.AddKafkaMessaging(configuration);
        services.AddPostgresOutbox(configuration);
        services.AddAuditWriter("config");
        services.AddSingleton<IConfigStore, PostgresConfigStore>();
        return services;
    }
}
