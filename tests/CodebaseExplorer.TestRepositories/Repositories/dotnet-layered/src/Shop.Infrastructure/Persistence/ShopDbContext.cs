using Microsoft.EntityFrameworkCore;
using Shop.Domain.Orders;

namespace Shop.Infrastructure.Persistence;

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
