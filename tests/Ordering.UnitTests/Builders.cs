using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.UnitTests.Domain;

public class AddressBuilder
{
    public Address Build()
    {
        return new Address("street", "city", "state", "country", "zipcode");
    }
}

public class OrderBuilder
{
    private readonly Order order;

    public OrderBuilder(Address address)
    {
        order = new Order(
            "userId",
            "fakeName",
            address,
            cardTypeId: 5,
            cardNumber: "12",
            cardSecurityNumber: "123",
            cardHolderName: "name",
            cardExpiration: DateTime.UtcNow);
    }

    public OrderBuilder AddOne(
        int productId,
        string productName,
        decimal unitPrice,
        decimal discount,
        string pictureUrl,
        int units = 1)
    {
        order.AddOrderItem(productId, productName, unitPrice, discount, pictureUrl, units);
        return this;
    }

    public OrderBuilder WithStatus(OrderStatus targetStatus)
    {
        // Drive the aggregate through its existing Set*Status() methods to reach
        // the target status realistically, rather than reflecting into the private setter.
        if (targetStatus == OrderStatus.Submitted)
        {
            return this;
        }

        order.SetAwaitingValidationStatus();
        if (targetStatus == OrderStatus.AwaitingValidation)
        {
            return this;
        }

        if (targetStatus == OrderStatus.Cancelled)
        {
            // Cancel from AwaitingValidation, the last status still eligible for cancellation.
            order.SetCancelledStatus();
            return this;
        }

        order.SetStockConfirmedStatus();
        if (targetStatus == OrderStatus.StockConfirmed)
        {
            return this;
        }

        order.SetPaidStatus();
        if (targetStatus == OrderStatus.Paid)
        {
            return this;
        }

        if (targetStatus == OrderStatus.Shipped)
        {
            order.SetShippedStatus();
            return this;
        }

        throw new ArgumentOutOfRangeException(nameof(targetStatus), targetStatus, "Unsupported target order status.");
    }

    public Order Build()
    {
        return order;
    }
}
