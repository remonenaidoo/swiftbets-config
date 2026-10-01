using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SwiftBets.Config.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddConfigApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ConfigHandler>();
        return services;
    }
}
