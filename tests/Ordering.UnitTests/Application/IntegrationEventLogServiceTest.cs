namespace eShop.Ordering.UnitTests.Application;

using eShop.IntegrationEventLogEF;
using eShop.IntegrationEventLogEF.Services;
using eShop.Ordering.API.Application.IntegrationEvents.Events;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;

// REQ-005
[TestClass]
public class IntegrationEventLogServiceTest
{
    [TestMethod("REQ-005 Pending outbox entries deserialize when the entry assembly does not define the event type")]
    public async Task RetrieveEventLogsPendingToPublish_ResolvesEventType_FromContextAssemblyNotEntryAssembly()
    {
        // This test assembly is the entry assembly, exactly as under WebApplicationFactory; it defines
        // no *IntegrationEvent types, so resolution must come from elsewhere.
        Assert.IsFalse(typeof(IntegrationEventLogServiceTest).Assembly.GetTypes().Any(t => t.Name.EndsWith("IntegrationEvent")));

        var options = new DbContextOptionsBuilder<OrderingContext>()
            .UseInMemoryDatabase(nameof(RetrieveEventLogsPendingToPublish_ResolvesEventType_FromContextAssemblyNotEntryAssembly))
            .Options;
        await using var context = new OrderingContext(options);
        var transactionId = Guid.NewGuid();
        var @event = new OrderStatusChangedToCancelledIntegrationEvent(42, OrderStatus.Cancelled, "buyer", "buyer-identity");
        context.Set<IntegrationEventLogEntry>().Add(new IntegrationEventLogEntry(@event, transactionId));
        await context.SaveChangesAsync();

        using var service = new IntegrationEventLogService<OrderingContext>(context);

        var pending = (await service.RetrieveEventLogsPendingToPublishAsync(transactionId)).ToList();

        Assert.HasCount(1, pending);
        var resolved = Assert.IsInstanceOfType<OrderStatusChangedToCancelledIntegrationEvent>(pending[0].IntegrationEvent);
        Assert.AreEqual(42, resolved.OrderId);
    }
}
