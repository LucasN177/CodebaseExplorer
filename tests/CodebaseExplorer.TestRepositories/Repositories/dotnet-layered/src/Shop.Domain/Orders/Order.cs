namespace Shop.Domain.Orders;

public sealed class Order
{
    public Order(Guid id, string customer)
    {
        Id = id;
        Customer = customer;
    }

    public Guid Id { get; }

    public string Customer { get; }

    public List<OrderLine> Lines { get; } = [];
}
