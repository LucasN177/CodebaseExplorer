using Shop.Domain.Orders;

namespace Shop.Infrastructure.Persistence;

public sealed class SqliteOrderRepository(ShopDbContext context) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
    }
}
