using Advanced.Core.Abstractions;
using Advanced.Core.Entities;

namespace Advanced.Services.Customers;

internal sealed class InMemoryCustomerRepository : IRepository<Customer>
{
    private readonly Dictionary<Guid, Customer> _items = new();

    public Customer? Find(Guid id) => _items.TryGetValue(id, out var customer) ? customer : null;

    public void Add(Customer item) => _items[item.Id] = item;
}
