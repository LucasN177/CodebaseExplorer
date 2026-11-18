using Advanced.Core.Abstractions;
using Advanced.Core.Entities;

namespace Advanced.Services.Customers;

public interface ICustomerService
{
    Result<Customer> Create(string name);
}
