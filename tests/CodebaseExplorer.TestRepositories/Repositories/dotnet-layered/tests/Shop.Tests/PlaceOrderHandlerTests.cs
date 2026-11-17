using NSubstitute;
using Shop.Application.Orders;
using Shop.Domain.Orders;
using Xunit;

namespace Shop.Tests;

public sealed class PlaceOrderHandlerTests
{
    [Fact]
    public async Task HandleAsync_AddsOrder()
    {
        var repository = Substitute.For<IOrderRepository>();
        var handler = new PlaceOrderHandler(repository);

        await handler.HandleAsync("alice", CancellationToken.None);

        await repository.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
