using Shop.Domain.Orders;

namespace Shop.Application.Orders;

public sealed class PlaceOrderHandler(IOrderRepository repository)
{
    public async Task<Guid> HandleAsync(string customer, CancellationToken cancellationToken)
    {
        var order = new Order(Guid.NewGuid(), customer);
        await repository.AddAsync(order, cancellationToken);
        return order.Id;
    }
}
