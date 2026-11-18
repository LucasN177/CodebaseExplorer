using Advanced.Core.Abstractions;
using Advanced.Core.Entities;
using Advanced.Services.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace Advanced.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdvancedServices(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddSingleton<IRepository<Customer>, InMemoryCustomerRepository>();

        // Negative cases for DI heuristics: a comment, a string and an unrelated method.
        // services.AddTransient<ICustomerService, CustomerService>();
        var example = "services.AddTransient<ICustomerService, CustomerService>()";
        new Catalog().AddSingleton(example);

        return services;
    }

    private sealed class Catalog
    {
        public void AddSingleton(string entry)
        {
        }
    }
}
