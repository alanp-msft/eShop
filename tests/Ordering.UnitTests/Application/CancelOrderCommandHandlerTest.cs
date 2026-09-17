namespace eShop.Ordering.UnitTests.Application;

using System.Diagnostics.Metrics;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.Domain.Seedwork;

// REQ-002, REQ-008, REQ-009
[TestClass]
public class CancelOrderCommandHandlerTest
{
    private readonly IOrderRepository _orderRepositoryMock;
    private readonly IIdentityService _identityServiceMock;
    private readonly IBuyerRepository _buyerRepositoryMock;
    private readonly RecordingLogger _loggerMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IMeterFactory _meterFactory;

    public CancelOrderCommandHandlerTest()
    {
        _orderRepositoryMock = Substitute.For<IOrderRepository>();
        _identityServiceMock = Substitute.For<IIdentityService>();
        _buyerRepositoryMock = Substitute.For<IBuyerRepository>();
        _loggerMock = new RecordingLogger();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _meterFactory = new TestMeterFactory();

        _orderRepositoryMock.UnitOfWork.Returns(_unitOfWorkMock);
    }

    private CancelOrderCommandHandler CreateHandler()
        => new(_orderRepositoryMock, _identityServiceMock, _buyerRepositoryMock, _loggerMock, TimeProvider.System, _meterFactory);

    /// <summary>
    /// REQ-009: minimal <see cref="IMeterFactory"/> that creates a real <see cref="Meter"/> per call
    /// (unlike the production factory, which caches by name) so tests observe the exact instruments
    /// the handler publishes under the shared <see cref="CancelOrderCommandHandler.MeterName"/> name.
    /// </summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Records long-valued measurements published on <see cref="CancelOrderCommandHandler.MeterName"/>
    /// via a <see cref="MeterListener"/>, capturing the optional "reason" tag for rejected counters.
    /// </summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener;

        public List<(string InstrumentName, long Value, IReadOnlyDictionary<string, string> Tags)> Measurements { get; } = [];

        public MetricRecorder()
        {
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CancelOrderCommandHandler.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            {
                var captured = new Dictionary<string, string>();
                foreach (var tag in tags)
                {
                    captured[tag.Key] = tag.Value?.ToString();
                }

                Measurements.Add((instrument.Name, value, captured));
            });
            _listener.Start();
        }

        /// <summary>Asserts the recorder saw exactly one measurement, on the named instrument, with value 1 and the given tags.</summary>
        public void AssertSingle(string instrumentName, params (string Key, string Value)[] tags)
        {
            Assert.HasCount(1, Measurements, "exactly one measurement across all instruments");
            var (name, value, captured) = Measurements[0];
            Assert.AreEqual(instrumentName, name);
            Assert.AreEqual(1, value);
            Assert.AreEqual(tags.Length, captured.Count);
            foreach (var (key, expected) in tags)
            {
                Assert.AreEqual(expected, captured[key], $"tag {key}");
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Minimal <see cref="ILogger{T}"/> test double that captures the eventId and the structured
    /// state pairs synchronously as each Log call happens, so assertions do not depend on the
    /// lifetime of any pooled logger state the <c>[LoggerMessage]</c> source generator may reuse
    /// after the call returns.
    /// </summary>
    private sealed class RecordingLogger : ILogger<CancelOrderCommandHandler>
    {
        public EventId? LastEventId { get; private set; }

        public IReadOnlyDictionary<string, object> LastState { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            LastEventId = eventId;
            if (state is IReadOnlyList<KeyValuePair<string, object>> structuredState)
            {
                LastState = structuredState.ToDictionary(kv => kv.Key, kv => kv.Value);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static Order CreateOrder(int? buyerId, OrderStatus status)
    {
        var address = new Address("street", "city", "state", "country", "zipcode");
        var order = new Order(
            "userId",
            "fakeName",
            address,
            cardTypeId: 5,
            cardNumber: "12",
            cardSecurityNumber: "123",
            cardHolderName: "name",
            cardExpiration: DateTime.UtcNow,
            buyerId: buyerId);

        // Drive the aggregate through its existing Set*Status() methods to reach the
        // target status realistically, mirroring tests/Ordering.UnitTests/Builders.cs.
        switch (status)
        {
            case OrderStatus.Submitted:
                break;
            case OrderStatus.AwaitingValidation:
                order.SetAwaitingValidationStatus();
                break;
            case OrderStatus.StockConfirmed:
                order.SetAwaitingValidationStatus();
                order.SetStockConfirmedStatus();
                break;
            case OrderStatus.Paid:
                order.SetAwaitingValidationStatus();
                order.SetStockConfirmedStatus();
                order.SetPaidStatus();
                break;
            case OrderStatus.Shipped:
                order.SetAwaitingValidationStatus();
                order.SetStockConfirmedStatus();
                order.SetPaidStatus();
                order.SetShippedStatus();
                break;
            case OrderStatus.Cancelled:
                order.SetAwaitingValidationStatus();
                order.SetCancelledStatus();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported target order status.");
        }

        return order;
    }

    [TestMethod("REQ-002 Handle returns Forbidden when the caller's identity does not match the order's buyer")]
    public async Task Handle_ReturnsForbidden_WhenCallerDoesNotOwnOrder()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("caller-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.Forbidden, result);
        Assert.AreEqual(OrderStatus.Submitted, order.OrderStatus);
        await _unitOfWorkMock.DidNotReceive().SaveEntitiesAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod("REQ-002 Handle returns Forbidden when the order has no buyer, the buyer cannot be loaded, or the caller has no identity")]
    [DataRow(false, true, "caller-identity", DisplayName = "order has no BuyerId")]
    [DataRow(true, false, "caller-identity", DisplayName = "buyer lookup returns null")]
    [DataRow(true, true, null, DisplayName = "caller identity is null")]
    public async Task Handle_ReturnsForbidden_ForEveryNonMatchingOwnershipBranch(bool orderHasBuyer, bool buyerExists, string callerIdentity)
    {
        // Arrange
        var order = CreateOrder(buyerId: orderHasBuyer ? 7 : null, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns(callerIdentity);
        _buyerRepositoryMock.FindByIdAsync(7).Returns(buyerExists ? new Buyer("owner-identity", "owner") : null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.Forbidden, result);
        Assert.AreEqual(OrderStatus.Submitted, order.OrderStatus);
        await _unitOfWorkMock.DidNotReceive().SaveEntitiesAsync(Arg.Any<CancellationToken>());
        Assert.IsNull(_loggerMock.LastEventId, "no audit entry is written for a forbidden attempt");
    }

    [TestMethod("REQ-002 Handle returns Success and cancels the order when the caller owns it")]
    public async Task Handle_ReturnsSuccess_WhenCallerOwnsEligibleOrder()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));
        _unitOfWorkMock.SaveEntitiesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.Success, result);
        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        await _unitOfWorkMock.Received(1).SaveEntitiesAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod("REQ-001 Handle returns NotFound when the order does not exist")]
    public async Task Handle_ReturnsNotFound_WhenOrderDoesNotExist()
    {
        // Arrange
        _orderRepositoryMock.GetAsync(Arg.Any<int>()).Returns((Order)null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.NotFound, result);
    }

    [TestMethod("REQ-001 Handle returns IneligibleStatus without calling SetCancelledStatus for StockConfirmed, Paid, and Shipped orders")]
    [DataRow(OrderStatus.StockConfirmed)]
    [DataRow(OrderStatus.Paid)]
    [DataRow(OrderStatus.Shipped)]
    public async Task Handle_ReturnsIneligibleStatus_ForIneligibleOrderStatuses(OrderStatus status)
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, status);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.IneligibleStatus, result);
        Assert.AreEqual(status, order.OrderStatus);
        await _unitOfWorkMock.DidNotReceive().SaveEntitiesAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod("REQ-007 Handle returns AlreadyCancelled without re-raising a domain event when the order is already Cancelled")]
    public async Task Handle_ReturnsAlreadyCancelled_WhenOrderAlreadyCancelled()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Cancelled);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert: distinguish the handler short-circuit from a domain no-op by asserting
        // persistence was never invoked (event-count assertions alone cannot prove this;
        // see .copilot-tracking/reviews/rpi/2026-09-16/order-cancellation-p01-validation.md finding 4).
        Assert.AreEqual(CancelOrderResult.AlreadyCancelled, result);
        await _unitOfWorkMock.DidNotReceive().SaveEntitiesAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod("REQ-008 Handle resolves the acting buyer's identity before cancelling")]
    public async Task Handle_ResolvesActingIdentity_BeforeCancelling()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));
        _unitOfWorkMock.SaveEntitiesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var handler = CreateHandler();

        // Act
        await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        _identityServiceMock.Received(1).GetUserIdentity();
    }

    [TestMethod("REQ-008 Handle logs a structured audit entry with OrderId and the acting identity on successful cancellation")]
    public async Task Handle_LogsStructuredAuditEntry_OnSuccessfulCancellation()
    {
        // Arrange
        const int orderNumber = 42;
        const string buyerIdentity = "owner-identity";
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(orderNumber).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns(buyerIdentity);
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer(buyerIdentity, "owner"));
        _unitOfWorkMock.SaveEntitiesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var handler = CreateHandler();

        // Act
        await handler.Handle(new CancelOrderCommand(orderNumber), default);

        // Assert: verify via the captured ILogger's structured state (eventId + named tag
        // values), not by string-matching the rendered message text.
        Assert.IsNotNull(_loggerMock.LastEventId);
        Assert.AreEqual(4, _loggerMock.LastEventId.Value.Id);
        Assert.AreEqual("OrderCancelledByCustomer", _loggerMock.LastEventId.Value.Name);
        Assert.IsNotNull(_loggerMock.LastState);
        Assert.AreEqual(orderNumber, _loggerMock.LastState["OrderId"]);
        Assert.AreEqual(buyerIdentity, _loggerMock.LastState["BuyerIdentity"]);
    }

    [TestMethod("REQ-009 Handle increments the succeeded counter exactly once for a successful cancellation")]
    [DoNotParallelize]
    public async Task Handle_IncrementsSucceededCounter_OnSuccessfulCancellation()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));
        _unitOfWorkMock.SaveEntitiesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        using var recorder = new MetricRecorder();
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.Success, result);
        recorder.AssertSingle("order_cancellations_succeeded", ("outcome", "cancelled"));
    }

    [TestMethod("REQ-009 Handle increments the succeeded counter with outcome=already_cancelled for an idempotent retry")]
    [DoNotParallelize]
    public async Task Handle_IncrementsSucceededCounter_WithOutcomeAlreadyCancelled_ForIdempotentRetry()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Cancelled);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));

        using var recorder = new MetricRecorder();
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.AlreadyCancelled, result);
        recorder.AssertSingle("order_cancellations_succeeded", ("outcome", "already_cancelled"));
    }

    [TestMethod("REQ-009 Handle increments the rejected counter with reason=ineligible_status for a Paid order")]
    [DoNotParallelize]
    public async Task Handle_IncrementsRejectedCounter_WithReasonIneligibleStatus_ForPaidOrder()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Paid);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));

        using var recorder = new MetricRecorder();
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.IneligibleStatus, result);
        recorder.AssertSingle("order_cancellations_rejected", ("reason", "ineligible_status"));
    }

    [TestMethod("REQ-009 Handle increments the rejected counter with reason=forbidden for a non-owner cancellation attempt")]
    [DoNotParallelize]
    public async Task Handle_IncrementsRejectedCounter_WithReasonForbidden_ForNonOwnerAttempt()
    {
        // Arrange: a non-owner attempt must still be observable via the rejected{reason=forbidden}
        // counter since the HTTP response is now a silent 404 indistinguishable from NotFound
        // (see .copilot-tracking/reviews/rpi/2026-09-16/order-cancellation-p03-validation.md finding 6).
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("caller-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));

        using var recorder = new MetricRecorder();
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new CancelOrderCommand(1), default);

        // Assert
        Assert.AreEqual(CancelOrderResult.Forbidden, result);
        recorder.AssertSingle("order_cancellations_rejected", ("reason", "forbidden"));
    }

    [TestMethod("REQ-009 Handle increments the failed counter and rethrows when an unexpected exception occurs")]
    [DoNotParallelize]
    public async Task Handle_IncrementsFailedCounter_AndRethrows_OnUnexpectedException()
    {
        // Arrange
        _orderRepositoryMock.GetAsync(Arg.Any<int>()).Returns<Task<Order>>(_ => throw new InvalidOperationException("boom"));

        using var recorder = new MetricRecorder();
        var handler = CreateHandler();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => handler.Handle(new CancelOrderCommand(1), default));

        recorder.AssertSingle("order_cancellations_failed");
    }

    [TestMethod("REQ-009 Handle does not count a caller-cancelled request as failed")]
    [DoNotParallelize]
    public async Task Handle_DoesNotIncrementFailedCounter_WhenCallerCancelsRequest()
    {
        // Arrange
        var order = CreateOrder(buyerId: 7, OrderStatus.Submitted);
        _orderRepositoryMock.GetAsync(1).Returns(order);
        _identityServiceMock.GetUserIdentity().Returns("owner-identity");
        _buyerRepositoryMock.FindByIdAsync(7).Returns(new Buyer("owner-identity", "owner"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _unitOfWorkMock.SaveEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new OperationCanceledException(cts.Token));

        using var recorder = new MetricRecorder();
        var handler = CreateHandler();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => handler.Handle(new CancelOrderCommand(1), cts.Token));

        Assert.IsEmpty(recorder.Measurements, "a caller-cancelled request records no measurement");
    }
}
