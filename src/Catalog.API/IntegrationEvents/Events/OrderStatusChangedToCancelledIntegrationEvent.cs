namespace eShop.Catalog.API.IntegrationEvents.Events;

// Mirrors eShop.Ordering.API.Application.IntegrationEvents.Events.OrderStatusChangedToCancelledIntegrationEvent
// property-for-property (names, types, constructor parameter order) so the RabbitMQ event bus, which
// routes and deserializes by type name, can materialize the payload published by Ordering.API.
public record OrderStatusChangedToCancelledIntegrationEvent : IntegrationEvent
{
    public int OrderId { get; }
    public OrderStatus OrderStatus { get; }
    public string BuyerName { get; }
    public string BuyerIdentityGuid { get; }

    public OrderStatusChangedToCancelledIntegrationEvent
        (int orderId, OrderStatus orderStatus, string buyerName, string buyerIdentityGuid)
    {
        OrderId = orderId;
        OrderStatus = orderStatus;
        BuyerName = buyerName;
        BuyerIdentityGuid = buyerIdentityGuid;
    }
}
