namespace Advanced.Core.Entities;

public partial class Customer
{
    private readonly List<OrderSummary> _orders = new();

    public IReadOnlyList<OrderSummary> Orders => _orders;

    public void AddOrder(decimal total)
    {
        _orders.Add(new OrderSummary(total));
    }

    public sealed class OrderSummary
    {
        public OrderSummary(decimal total)
        {
            Total = total;
        }

        public decimal Total { get; }
    }
}
