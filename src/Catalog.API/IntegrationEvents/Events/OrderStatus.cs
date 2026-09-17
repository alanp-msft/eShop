using System.Text.Json.Serialization;

namespace eShop.Catalog.API.IntegrationEvents.Events;

// Mirrors eShop.Ordering.Domain.AggregatesModel.OrderAggregate.OrderStatus so
// OrderStatusChangedToCancelledIntegrationEvent deserializes the value published by Ordering.API.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderStatus
{
    Submitted = 1,
    AwaitingValidation = 2,
    StockConfirmed = 3,
    Paid = 4,
    Shipped = 5,
    Cancelled = 6
}
