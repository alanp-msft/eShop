using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.Ordering.API.Application.Commands;
using eShop.Ordering.API.Application.Models;
using eShop.Ordering.API.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using DomainOrderStatus = eShop.Ordering.Domain.AggregatesModel.OrderAggregate.OrderStatus;

namespace eShop.Ordering.FunctionalTests;

public sealed class OrderingApiTests : IClassFixture<OrderingApiFixture>
{
    private readonly OrderingApiFixture _fixture;
    private readonly WebApplicationFactory<Program> _webApplicationFactory;
    private readonly HttpClient _httpClient;

    public OrderingApiTests(OrderingApiFixture fixture)
    {
        var handler = new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0));

        _fixture = fixture;
        _webApplicationFactory = fixture;
        _httpClient = _webApplicationFactory.CreateDefaultClient(handler);
    }

    [Fact]
    public async Task GetAllStoredOrdersWorks()
    {
        // Act
        var response = await _httpClient.GetAsync("api/orders", TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // REQ-008
    [Fact(DisplayName = "REQ-008 CancelOrderCommandHandler resolves from the Ordering.API container, including TimeProvider")]
    public void CancelOrderCommandHandlerResolvesFromContainer()
    {
        // WebApplicationBuilder registers no TimeProvider; without the explicit Ordering.API
        // registration the handler fails only at the first cancel request, which unit tests cannot see.
        using var scope = _webApplicationFactory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<CancelOrderCommand, CancelOrderResult>>();

        Assert.IsType<CancelOrderCommandHandler>(handler);
    }

    [Fact]
    public async Task CancelWithEmptyGuidFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.Empty.ToString() } }
        };
        var response = await _httpClient.PutAsync("/api/orders/cancel", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelNonExistentOrderFails()
    {
        // REQ-002, REQ-009: CancelOrderResult.NotFound maps to 404, not the 500 fallback reserved for
        // an unmatched/unknown handler result.
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PutAsync("api/orders/cancel", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // REQ-002
    [Fact(DisplayName = "REQ-002 Cancelling another buyer's order returns 404 indistinguishable from a missing order and leaves the order unchanged")]
    public async Task CancelAnotherBuyersOrderReturns404IndistinguishableFromMissingOrderAndLeavesOrderUnchanged()
    {
        // Arrange
        var (_, otherBuyerOrderId) = await _fixture.SeedOwnerAndOtherBuyerOrdersAsync(TestContext.Current.CancellationToken);

        // Act: cancel the other buyer's order (caller is always AutoAuthorizeMiddleware.IDENTITY_ID)
        // and, separately, cancel a non-existent order number, then compare the response bodies.
        var forbiddenResponse = await SendCancelRequestAsync(otherBuyerOrderId, Guid.NewGuid());
        var notFoundResponse = await SendCancelRequestAsync(-1, Guid.NewGuid());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, forbiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);

        var forbiddenBody = await NormalizedProblemBodyAsync(forbiddenResponse);
        var notFoundBody = await NormalizedProblemBodyAsync(notFoundResponse);
        Assert.Equal(notFoundBody, forbiddenBody);

        var otherBuyerOrderStatus = await _fixture.GetOrderStatusAsync(otherBuyerOrderId, TestContext.Current.CancellationToken);
        Assert.Equal(DomainOrderStatus.Submitted, otherBuyerOrderStatus);
    }

    // REQ-005
    [Fact(DisplayName = "REQ-005 Cancelling an eligible order publishes exactly one OrderStatusChangedToCancelledIntegrationEvent to the outbox")]
    public async Task CancellingEligibleOrderPublishesExactlyOneCancelledIntegrationEvent()
    {
        // Arrange
        var (ownerOrderId, _) = await _fixture.SeedOwnerAndOtherBuyerOrdersAsync(TestContext.Current.CancellationToken);

        // Act
        var response = await SendCancelRequestAsync(ownerOrderId, Guid.NewGuid());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var outboxCount = await _fixture.CountCancelledIntegrationEventLogEntriesAsync(ownerOrderId, TestContext.Current.CancellationToken);
        Assert.Equal(1, outboxCount);
    }

    // REQ-007
    [Fact(DisplayName = "REQ-007 Repeating the same cancel request id returns the original success result without a second outbox entry")]
    public async Task RepeatingSameCancelRequestIdReturnsOriginalSuccessResultWithoutSecondOutboxEntry()
    {
        // Arrange
        var (ownerOrderId, _) = await _fixture.SeedOwnerAndOtherBuyerOrdersAsync(TestContext.Current.CancellationToken);
        var requestId = Guid.NewGuid();

        // Act
        var firstResponse = await SendCancelRequestAsync(ownerOrderId, requestId);
        var secondResponse = await SendCancelRequestAsync(ownerOrderId, requestId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var outboxCount = await _fixture.CountCancelledIntegrationEventLogEntriesAsync(ownerOrderId, TestContext.Current.CancellationToken);
        Assert.Equal(1, outboxCount);
    }

    // REQ-007
    [Fact(DisplayName = "REQ-007 Cancelling an already-Cancelled order with a new request id succeeds without a second outbox entry")]
    public async Task CancellingAlreadyCancelledOrderWithNewRequestIdSucceedsWithoutSecondOutboxEntry()
    {
        // Arrange
        var (ownerOrderId, _) = await _fixture.SeedOwnerAndOtherBuyerOrdersAsync(TestContext.Current.CancellationToken);
        var firstResponse = await SendCancelRequestAsync(ownerOrderId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act: a new x-requestid against the now-Cancelled order (ADR Option C2 no-op guard).
        var secondResponse = await SendCancelRequestAsync(ownerOrderId, Guid.NewGuid());

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var outboxCount = await _fixture.CountCancelledIntegrationEventLogEntriesAsync(ownerOrderId, TestContext.Current.CancellationToken);
        Assert.Equal(1, outboxCount);
    }

    [Fact]
    public async Task ShipWithEmptyGuidFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.Empty.ToString() } }
        };
        var response = await _httpClient.PutAsync("api/orders/ship", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShipNonExistentOrderFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PutAsync("api/orders/ship", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetAllOrdersCardType()
    {
        // Act 1
        var response = await _httpClient.GetAsync("api/orders/cardtypes", TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetStoredOrdersWithOrderId()
    {
        // Act: an id no seeded or created order can reach, so this stays 404 regardless of test order.
        var response = await _httpClient.GetAsync($"api/orders/{int.MaxValue}", TestContext.Current.CancellationToken);
        var responseStatus = response.StatusCode;

        // Assert
        Assert.Equal("NotFound", responseStatus.ToString());
    }

    [Fact]
    public async Task AddNewEmptyOrder()
    {
        // Act
        var content = new StringContent(JsonSerializer.Serialize(new Order()), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.Empty.ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddNewOrder()
    {
        // Act
        var item = new BasketItem
        {
            Id = "1",
            ProductId = 12,
            ProductName = "Test",
            UnitPrice = 10,
            OldUnitPrice = 9,
            Quantity = 1,
            PictureUrl = null
        };
        var cardExpirationDate = Convert.ToDateTime("2023-12-22T12:34:24.334Z");
        var OrderRequest = new CreateOrderRequest("1", "TestUser", null, null, null, null, null, "XXXXXXXXXXXX0005", "Test User", cardExpirationDate, "test buyer", 1, null, new List<BasketItem> { item });
        var content = new StringContent(JsonSerializer.Serialize(OrderRequest), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostDraftOrder()
    {
        // Act
        var item = new BasketItem
        {
            Id = "1",
            ProductId = 12,
            ProductName = "Test",
            UnitPrice = 10,
            OldUnitPrice = 9,
            Quantity = 1,
            PictureUrl = null
        };
        var bodyContent = new CustomerBasket("1", new List<BasketItem> { item });
        var content = new StringContent(JsonSerializer.Serialize(bodyContent), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders/draft", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrderDraftSucceeds()
    {
        var payload = FakeOrderDraftCommand();
        var content = new StringContent(JsonSerializer.Serialize(FakeOrderDraftCommand()), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders/draft", content, TestContext.Current.CancellationToken);

        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var responseData = JsonSerializer.Deserialize<OrderDraftDTO>(s, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload.Items.Count(), responseData.OrderItems.Count());
        Assert.Equal(payload.Items.Sum(o => o.Quantity * o.UnitPrice), responseData.Total);
        AssertThatOrderItemsAreTheSameAsRequestPayloadItems(payload, responseData);
    }

    private CreateOrderDraftCommand FakeOrderDraftCommand()
    {
        return new CreateOrderDraftCommand(
            BuyerId: Guid.NewGuid().ToString(),
            new List<BasketItem>()
            {
                new BasketItem()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductId = 1,
                    ProductName = "Test Product 1",
                    UnitPrice = 10.2m,
                    OldUnitPrice = 9.8m,
                    Quantity = 2,
                    PictureUrl = Guid.NewGuid().ToString(),
                }
            });
    }

    private static void AssertThatOrderItemsAreTheSameAsRequestPayloadItems(CreateOrderDraftCommand payload, OrderDraftDTO responseData)
    {
        // check that OrderItems contain all product Ids from the payload
        var payloadItemsProductIds = payload.Items.Select(x => x.ProductId);
        var orderItemsProductIds = responseData.OrderItems.Select(x => x.ProductId);
        Assert.All(orderItemsProductIds, orderItemProdId => payloadItemsProductIds.Contains(orderItemProdId));
        // TODO: might need to add more asserts in here
    }

    string BuildOrder()
    {
        var order = new
        {
            OrderNumber = "-1"
        };
        return JsonSerializer.Serialize(order);
    }

    private async Task<HttpResponseMessage> SendCancelRequestAsync(int orderNumber, Guid requestId)
    {
        var content = new StringContent(JsonSerializer.Serialize(new { OrderNumber = orderNumber }), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", requestId.ToString() } }
        };
        return await _httpClient.PutAsync("api/orders/cancel", content, TestContext.Current.CancellationToken);
    }

    // REQ-002 (ADR amendment 2026-09-16): compares ProblemDetails bodies while ignoring the
    // "traceId" extension, which is unique per request and would otherwise make two functionally
    // identical 404 bodies compare unequal.
    private async Task<string> NormalizedProblemBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var node = JsonNode.Parse(body)!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }
}
