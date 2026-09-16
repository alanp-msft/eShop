namespace eShop.Ordering.UnitTests.Domain;

using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.Domain.Events;
using eShop.Ordering.UnitTests.Domain;

[TestClass]
public class OrderAggregateTest
{
    public OrderAggregateTest()
    { }

    [TestMethod]
    public void Create_order_item_success()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        //Act 
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        //Assert
        Assert.IsNotNull(fakeOrderItem);
    }

    [TestMethod]
    public void Invalid_number_of_units()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = -1;

        //Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units));
    }

    [TestMethod]
    public void Invalid_total_of_order_item_lower_than_discount_applied()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 1;
        
        //Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units));       
    }

    [TestMethod]
    public void Invalid_discount_setting()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        //Act 
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        //Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => fakeOrderItem.SetNewDiscount(-1));
    }

    [TestMethod]
    public void Invalid_units_setting()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        //Act 
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        //Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => fakeOrderItem.AddUnits(-1));
    }

    [TestMethod]
    public void when_add_two_times_on_the_same_item_then_the_total_of_order_should_be_the_sum_of_the_two_items()
    {
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address)
            .AddOne(1, "cup", 10.0m, 0, string.Empty)
            .AddOne(1, "cup", 10.0m, 0, string.Empty)
            .Build();

        Assert.AreEqual(20.0m, order.GetTotal());
    }

    [TestMethod]
    public void Add_new_Order_raises_new_event()
    {
        //Arrange
        var street = "fakeStreet";
        var city = "FakeCity";
        var state = "fakeState";
        var country = "fakeCountry";
        var zipcode = "FakeZipCode";
        var cardTypeId = 5;
        var cardNumber = "12";
        var cardSecurityNumber = "123";
        var cardHolderName = "FakeName";
        var cardExpiration = DateTime.UtcNow.AddYears(1);
        var expectedResult = 1;

        //Act 
        var fakeOrder = new Order("1", "fakeName", new Address(street, city, state, country, zipcode), cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);

        //Assert
        Assert.HasCount(expectedResult, fakeOrder.DomainEvents);
    }

    [TestMethod]
    public void Add_event_Order_explicitly_raises_new_event()
    {
        //Arrange   
        var street = "fakeStreet";
        var city = "FakeCity";
        var state = "fakeState";
        var country = "fakeCountry";
        var zipcode = "FakeZipCode";
        var cardTypeId = 5;
        var cardNumber = "12";
        var cardSecurityNumber = "123";
        var cardHolderName = "FakeName";
        var cardExpiration = DateTime.UtcNow.AddYears(1);
        var expectedResult = 2;

        //Act 
        var fakeOrder = new Order("1", "fakeName", new Address(street, city, state, country, zipcode), cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);
        fakeOrder.AddDomainEvent(new OrderStartedDomainEvent(fakeOrder, "fakeName", "1", cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration));
        //Assert
        Assert.HasCount(expectedResult, fakeOrder.DomainEvents);
    }

    [TestMethod]
    public void Remove_event_Order_explicitly()
    {
        //Arrange    
        var street = "fakeStreet";
        var city = "FakeCity";
        var state = "fakeState";
        var country = "fakeCountry";
        var zipcode = "FakeZipCode";
        var cardTypeId = 5;
        var cardNumber = "12";
        var cardSecurityNumber = "123";
        var cardHolderName = "FakeName";
        var cardExpiration = DateTime.UtcNow.AddYears(1);
        var fakeOrder = new Order("1", "fakeName", new Address(street, city, state, country, zipcode), cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);
        var @fakeEvent = new OrderStartedDomainEvent(fakeOrder, "1", "fakeName", cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);
        var expectedResult = 1;

        //Act         
        fakeOrder.AddDomainEvent(@fakeEvent);
        fakeOrder.RemoveDomainEvent(@fakeEvent);
        //Assert
        Assert.HasCount(expectedResult, fakeOrder.DomainEvents);
    }

    // REQ-003
    [TestMethod("REQ-003 SetCancelledStatus transitions Submitted to Cancelled and raises OrderCancelledDomainEvent")]
    public void SetCancelledStatus_from_Submitted_transitions_to_Cancelled_and_raises_event()
    {
        //Arrange
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address).WithStatus(OrderStatus.Submitted).Build();
        var eventCountBeforeCancel = order.DomainEvents.Count;

        //Act
        order.SetCancelledStatus();

        //Assert
        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        Assert.IsTrue(order.DomainEvents.Skip(eventCountBeforeCancel).Any(e => e is OrderCancelledDomainEvent));
    }

    // REQ-003
    [TestMethod("REQ-003 SetCancelledStatus transitions AwaitingValidation to Cancelled and raises OrderCancelledDomainEvent")]
    public void SetCancelledStatus_from_AwaitingValidation_transitions_to_Cancelled_and_raises_event()
    {
        //Arrange
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address).WithStatus(OrderStatus.AwaitingValidation).Build();
        var eventCountBeforeCancel = order.DomainEvents.Count;

        //Act
        order.SetCancelledStatus();

        //Assert
        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        Assert.IsTrue(order.DomainEvents.Skip(eventCountBeforeCancel).Any(e => e is OrderCancelledDomainEvent));
    }

    // REQ-001
    [TestMethod("REQ-001 SetCancelledStatus throws OrderingDomainException and leaves status unchanged for Paid orders")]
    public void SetCancelledStatus_from_Paid_throws_and_leaves_status_unchanged()
    {
        //Arrange
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address).WithStatus(OrderStatus.Paid).Build();

        //Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => order.SetCancelledStatus());
        Assert.AreEqual(OrderStatus.Paid, order.OrderStatus);
    }

    // REQ-001
    [TestMethod("REQ-001 SetCancelledStatus throws OrderingDomainException and leaves status unchanged for Shipped orders")]
    public void SetCancelledStatus_from_Shipped_throws_and_leaves_status_unchanged()
    {
        //Arrange
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address).WithStatus(OrderStatus.Shipped).Build();

        //Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => order.SetCancelledStatus());
        Assert.AreEqual(OrderStatus.Shipped, order.OrderStatus);
    }

    // REQ-007
    [TestMethod("REQ-007 SetCancelledStatus is a no-op and raises no additional domain event when the order is already Cancelled")]
    public void SetCancelledStatus_from_Cancelled_is_noop_and_raises_no_additional_event()
    {
        //Arrange
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address).WithStatus(OrderStatus.Cancelled).Build();
        var eventCountAfterFirstCancel = order.DomainEvents.Count;

        //Act
        order.SetCancelledStatus();

        //Assert
        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        Assert.HasCount(eventCountAfterFirstCancel, order.DomainEvents);
    }
}
