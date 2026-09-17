namespace eShop.Ordering.UnitTests.Application;

using Microsoft.AspNetCore.Http.HttpResults;
using eShop.Ordering.API.Application.Queries;
using Order = eShop.Ordering.API.Application.Queries.Order;
using NSubstitute.ExceptionExtensions;

[TestClass]
public class OrdersWebApiTest
{
    private readonly IMediator _mediatorMock;
    private readonly IOrderQueries _orderQueriesMock;
    private readonly IIdentityService _identityServiceMock;
    private readonly ILogger<OrderServices> _loggerMock;

    public OrdersWebApiTest()
    {
        _mediatorMock = Substitute.For<IMediator>();
        _orderQueriesMock = Substitute.For<IOrderQueries>();
        _identityServiceMock = Substitute.For<IIdentityService>();
        _loggerMock = Substitute.For<ILogger<OrderServices>>();
    }

    [TestMethod]
    public async Task Cancel_order_with_requestId_success()
    {
        // Arrange
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.Success));

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), orderServices);

        // Assert
        Assert.IsInstanceOfType<Ok>(result.Result);
    }

    [TestMethod]
    public async Task Cancel_order_bad_request()
    {
        // Arrange
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.Success));

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.CancelOrderAsync(Guid.Empty, new CancelOrderCommand(1), orderServices);

        // Assert
        Assert.IsInstanceOfType<BadRequest<string>>(result.Result);
    }

    [TestMethod]
    public async Task Ship_order_with_requestId_success()
    {
        // Arrange
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<ShipOrderCommand, bool>>(), default)
            .Returns(Task.FromResult(true));

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.ShipOrderAsync(Guid.NewGuid(), new ShipOrderCommand(1), orderServices);

        // Assert
        Assert.IsInstanceOfType<Ok>(result.Result);

    }

    [TestMethod]
    public async Task Ship_order_bad_request()
    {
        // Arrange
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CreateOrderCommand, bool>>(), default)
            .Returns(Task.FromResult(true));

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.ShipOrderAsync(Guid.Empty, new ShipOrderCommand(1), orderServices);

        // Assert
        Assert.IsInstanceOfType<BadRequest<string>>(result.Result);
    }

    [TestMethod]
    public async Task Get_orders_success()
    {
        // Arrange
        var fakeDynamicResult = Enumerable.Empty<OrderSummary>();

        _identityServiceMock.GetUserIdentity()
            .Returns(Guid.NewGuid().ToString());

        _orderQueriesMock.GetOrdersFromUserAsync(Guid.NewGuid().ToString())
            .Returns(Task.FromResult(fakeDynamicResult));

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.GetOrdersByUserAsync(orderServices);

        // Assert
        Assert.IsInstanceOfType<Ok<IEnumerable<OrderSummary>>>(result);
    }

    [TestMethod]
    public async Task Get_order_success()
    {
        // Arrange
        var fakeOrderId = 123;
        var fakeDynamicResult = new Order();
        _orderQueriesMock.GetOrderAsync(Arg.Any<int>())
            .Returns(Task.FromResult(fakeDynamicResult));

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.GetOrderAsync(fakeOrderId, orderServices);

        // Assert
        Assert.IsInstanceOfType<Ok<Order>>(result.Result);
        Assert.AreSame(fakeDynamicResult, ((Ok<Order>)result.Result).Value);
    }

    [TestMethod]
    public async Task Get_order_fails()
    {
        // Arrange
        var fakeOrderId = 123;
#pragma warning disable NS5003
        _orderQueriesMock.GetOrderAsync(Arg.Any<int>())
            .Throws(new KeyNotFoundException());
#pragma warning restore NS5003

        // Act
        var orderServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var result = await OrdersApi.GetOrderAsync(fakeOrderId, orderServices);

        // Assert
        Assert.IsInstanceOfType<NotFound>(result.Result);
    }

    [TestMethod]
    public async Task Get_cardTypes_success()
    {
        // Arrange
        var fakeDynamicResult = Enumerable.Empty<CardType>();
        _orderQueriesMock.GetCardTypesAsync()
            .Returns(Task.FromResult(fakeDynamicResult));

        // Act
        var result = await OrdersApi.GetCardTypesAsync(_orderQueriesMock);

        // Assert
        Assert.IsInstanceOfType<Ok<IEnumerable<CardType>>>(result);
        Assert.AreSame(fakeDynamicResult, result.Value);
    }

    [TestMethod]
    public async Task Cancel_order_returns_problem_for_unexpected_result()
    {
        // An undefined member proves the discard arm independently of the named Unknown value.
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult((CancelOrderResult)99));
        var services = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

        var result = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), services);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Result);
        Assert.AreEqual(500, problem.StatusCode);
    }

    [TestMethod("REQ-001 CancelOrderAsync returns 404 when the command result is NotFound")]
    public async Task Cancel_order_returns_not_found_when_command_result_is_not_found()
    {
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.NotFound));
        var services = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

        var result = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), services);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Result);
        Assert.AreEqual(404, problem.StatusCode);
    }

    [TestMethod("REQ-002 CancelOrderAsync returns 404 with the not-found body when the command result is Forbidden")]
    public async Task Cancel_order_returns_not_found_body_when_command_result_is_forbidden()
    {
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.NotFound));
        var notFoundServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var notFoundResult = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), notFoundServices);
        var notFoundProblem = Assert.IsInstanceOfType<ProblemHttpResult>(notFoundResult.Result);

        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.Forbidden));
        var forbiddenServices = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);
        var forbiddenResult = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), forbiddenServices);
        var forbiddenProblem = Assert.IsInstanceOfType<ProblemHttpResult>(forbiddenResult.Result);

        Assert.AreEqual(notFoundProblem.StatusCode, forbiddenProblem.StatusCode);
        Assert.AreEqual(notFoundProblem.ProblemDetails.Detail, forbiddenProblem.ProblemDetails.Detail);
        Assert.AreEqual(notFoundProblem.ProblemDetails.Title, forbiddenProblem.ProblemDetails.Title);
        Assert.AreEqual(notFoundProblem.ProblemDetails.Type, forbiddenProblem.ProblemDetails.Type);
        Assert.AreEqual(notFoundProblem.ProblemDetails.Instance, forbiddenProblem.ProblemDetails.Instance);
        Assert.AreEqual(notFoundProblem.ProblemDetails.Extensions.Count, forbiddenProblem.ProblemDetails.Extensions.Count);
    }

    [TestMethod("REQ-001 CancelOrderAsync returns 409 when the command result is IneligibleStatus")]
    public async Task Cancel_order_returns_conflict_when_command_result_is_ineligible_status()
    {
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.IneligibleStatus));
        var services = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

        var result = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), services);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Result);
        Assert.AreEqual(409, problem.StatusCode);
    }

    [TestMethod("REQ-001 CancelOrderAsync returns 200 when the command result is AlreadyCancelled")]
    public async Task Cancel_order_returns_ok_when_command_result_is_already_cancelled()
    {
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(CancelOrderResult.AlreadyCancelled));
        var services = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

        var result = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), services);

        Assert.IsInstanceOfType<Ok>(result.Result);
    }

    [TestMethod("REQ-009 CancelOrderAsync returns 500, not 404, when the command result is the default/Unknown value produced by a swallowed exception")]
    public async Task Cancel_order_returns_problem_not_not_found_when_command_result_is_unknown()
    {
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)
            .Returns(Task.FromResult(default(CancelOrderResult)));
        var services = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

        var result = await OrdersApi.CancelOrderAsync(Guid.NewGuid(), new CancelOrderCommand(1), services);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Result);
        Assert.AreEqual(500, problem.StatusCode);
        Assert.AreNotEqual(404, problem.StatusCode);
    }

    [TestMethod]
    public async Task Ship_order_returns_problem_when_command_fails()
    {
        _mediatorMock.Send(Arg.Any<IdentifiedCommand<ShipOrderCommand, bool>>(), default)
            .Returns(Task.FromResult(false));
        var services = new OrderServices(_mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

        var result = await OrdersApi.ShipOrderAsync(Guid.NewGuid(), new ShipOrderCommand(1), services);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result.Result);
        Assert.AreEqual(500, problem.StatusCode);
    }
}
