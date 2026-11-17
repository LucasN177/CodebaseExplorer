using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Orders;

namespace Shop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<PlaceOrderHandler>();
        return services;
    }
}
