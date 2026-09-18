namespace eShop.Ordering.API.Application.Commands;

using System.Diagnostics.Metrics;

// REQ-002, REQ-008, REQ-009
// Regular CommandHandler
public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    /// <summary>
    /// REQ-009: shared meter name registered by the host via
    /// <c>AddOpenTelemetry().WithMetrics(m => m.AddMeter(MeterName))</c> so tests and the production
    /// registration stay in sync.
    /// </summary>
    public const string MeterName = "eShop.Ordering.API";

    private readonly IOrderRepository _orderRepository;
    private readonly IIdentityService _identityService;
    private readonly IBuyerRepository _buyerRepository;
    private readonly ILogger<CancelOrderCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Counter<long> _succeededCounter;
    private readonly Counter<long> _rejectedCounter;
    private readonly Counter<long> _failedCounter;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IIdentityService identityService,
        IBuyerRepository buyerRepository,
        ILogger<CancelOrderCommandHandler> logger,
        TimeProvider timeProvider,
        IMeterFactory meterFactory)
    {
        _orderRepository = orderRepository;
        _identityService = identityService;
        _buyerRepository = buyerRepository;
        _logger = logger;
        _timeProvider = timeProvider;

        var meter = meterFactory.Create(MeterName);
        _succeededCounter = meter.CreateCounter<long>("order_cancellations_succeeded");
        _rejectedCounter = meter.CreateCounter<long>("order_cancellations_rejected");
        _failedCounter = meter.CreateCounter<long>("order_cancellations_failed");
    }

    /// <summary>
    /// Handler which processes the command when
    /// customer executes cancel order from app
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    public async Task<CancelOrderResult> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        // REQ-009: order_cancellations_failed covers unexpected exceptions from any branch below;
        // the exception is rethrown so IdentifiedCommandHandler's upstream handling is unchanged.
        try
        {
            var orderToUpdate = await _orderRepository.GetAsync(command.OrderNumber);
            if (orderToUpdate == null)
            {
                _rejectedCounter.Add(1, new KeyValuePair<string, object>("reason", "not_found"));
                return CancelOrderResult.NotFound;
            }

            var callerIdentity = _identityService.GetUserIdentity();

            var buyer = orderToUpdate.BuyerId.HasValue
                ? await _buyerRepository.FindByIdAsync(orderToUpdate.BuyerId.Value)
                : null;

            if (buyer is null || buyer.IdentityGuid != callerIdentity)
            {
                // REQ-009: rejected{reason=forbidden} is the only signal that detects order-enumeration
                // attempts since non-owners now receive the same body/status as NotFound (T-ORDERINGAPI-006).
                _rejectedCounter.Add(1, new KeyValuePair<string, object>("reason", "forbidden"));
                return CancelOrderResult.Forbidden;
            }

            if (orderToUpdate.OrderStatus == OrderStatus.Cancelled)
            {
                // Idempotent retries remain auditable even though no state changes.
                // REQ-009: an idempotent retry is a successful outcome, not a rejection; the outcome tag
                // lets the production gate reconcile fresh cancellations against outbox publishes.
                LogCancellationAudit(command.OrderNumber, callerIdentity);
                _succeededCounter.Add(1, new KeyValuePair<string, object>("outcome", "already_cancelled"));
                return CancelOrderResult.AlreadyCancelled;
            }

            if (orderToUpdate.OrderStatus == OrderStatus.StockConfirmed ||
                orderToUpdate.OrderStatus == OrderStatus.Paid ||
                orderToUpdate.OrderStatus == OrderStatus.Shipped)
            {
                _rejectedCounter.Add(1, new KeyValuePair<string, object>("reason", "ineligible_status"));
                return CancelOrderResult.IneligibleStatus;
            }

            orderToUpdate.SetCancelledStatus();
            await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
            LogCancellationAudit(command.OrderNumber, callerIdentity);
            // Counted when the handler reaches success; TransactionBehavior commits afterwards, so a
            // commit failure is not reflected here (known limitation, see the P05 change record).
            _succeededCounter.Add(1, new KeyValuePair<string, object>("outcome", "cancelled"));
            return CancelOrderResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A caller abandoning the request is not a server failure.
            throw;
        }
        catch
        {
            _failedCounter.Add(1);
            throw;
        }
    }

    private void LogCancellationAudit(int orderNumber, string buyerIdentity)
    {
        OrderingApiTrace.LogOrderCancelledByCustomer(
            _logger,
            orderNumber,
            buyerIdentity,
            _timeProvider.GetUtcNow().UtcDateTime);
    }
}


// Use for Idempotency in Command process
public class CancelOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CancelOrderCommand, CancelOrderResult>
{
    public CancelOrderIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CancelOrderCommand, CancelOrderResult>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override CancelOrderResult CreateResultForDuplicateRequest()
    {
        return CancelOrderResult.Success; // Ignore duplicate requests for processing order.
    }
}
