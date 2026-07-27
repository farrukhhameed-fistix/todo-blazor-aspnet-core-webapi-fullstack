using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.WebApi.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Fistix.TaskManager.WebApi.Extensions;

public static class SignalRServiceExtension
{
    public static IServiceCollection AddClassificationSignalR(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IClassificationNotifier, SignalRClassificationNotifier>();
        services.AddSingleton<IAiBatchNotifier, SignalRAiBatchNotifier>();
        return services;
    }
}
