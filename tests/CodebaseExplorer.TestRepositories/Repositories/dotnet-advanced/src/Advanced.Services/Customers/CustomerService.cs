using Advanced.Core.Abstractions;
using Advanced.Core.Entities;

namespace Advanced.Services.Customers;

internal sealed class CustomerService : ICustomerService
{
    private readonly IRepository<Customer> _repository;

    public CustomerService(IRepository<Customer> repository)
    {
        _repository = repository;
    }

    public Result<Customer> Create(string name)
    {
        var customer = new Customer(Guid.NewGuid(), name);
        _repository.Add(customer);
        return Result<Customer>.Ok(customer);
    }
}
