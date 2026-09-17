namespace eShop.Catalog.API.IntegrationEvents.EventHandling;

// REQ-004
public class OrderStatusChangedToCancelledIntegrationEventHandler(
    ILogger<OrderStatusChangedToCancelledIntegrationEventHandler> logger) :
    IIntegrationEventHandler<OrderStatusChangedToCancelledIntegrationEvent>
{
    public Task Handle(OrderStatusChangedToCancelledIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        // REQ-004 / Assumption A1: AvailableStock is only ever decremented once an order reaches
        // Paid (see OrderStatusChangedToPaidIntegrationEventHandler), and cancellation is only
        // reachable from Submitted or AwaitingValidation orders (REQ-001). No stock reservation was
        // ever taken for an order that can legally reach Cancelled, so there is nothing to release
        // here. This handler exists purely as the downstream "stock-release signal" consumer required
        // by REQ-004, for observability and future extensibility, and deliberately takes no
        // CatalogContext dependency so no DbContext is constructed per message.
        return Task.CompletedTask;
    }
}
