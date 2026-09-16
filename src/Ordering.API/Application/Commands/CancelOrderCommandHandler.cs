namespace eShop.Ordering.API.Application.Commands;

// REQ-002, REQ-008
// Regular CommandHandler
public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdentityService _identityService;
    private readonly IBuyerRepository _buyerRepository;
    private readonly ILogger<CancelOrderCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IIdentityService identityService,
        IBuyerRepository buyerRepository,
        ILogger<CancelOrderCommandHandler> logger,
        TimeProvider timeProvider)
    {
        _orderRepository = orderRepository;
        _identityService = identityService;
        _buyerRepository = buyerRepository;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Handler which processes the command when
    /// customer executes cancel order from app
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    public async Task<CancelOrderResult> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var orderToUpdate = await _orderRepository.GetAsync(command.OrderNumber);
        if (orderToUpdate == null)
        {
            return CancelOrderResult.NotFound;
        }

        var callerIdentity = _identityService.GetUserIdentity();

        var buyer = orderToUpdate.BuyerId.HasValue
            ? await _buyerRepository.FindByIdAsync(orderToUpdate.BuyerId.Value)
            : null;

        if (buyer is null || buyer.IdentityGuid != callerIdentity)
        {
            return CancelOrderResult.Forbidden;
        }

        if (orderToUpdate.OrderStatus == OrderStatus.Cancelled)
        {
            // Idempotent retries remain auditable even though no state changes.
            LogCancellationAudit(command.OrderNumber, callerIdentity);
            return CancelOrderResult.AlreadyCancelled;
        }

        if (orderToUpdate.OrderStatus == OrderStatus.StockConfirmed ||
            orderToUpdate.OrderStatus == OrderStatus.Paid ||
            orderToUpdate.OrderStatus == OrderStatus.Shipped)
        {
            return CancelOrderResult.IneligibleStatus;
        }

        orderToUpdate.SetCancelledStatus();
        await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        LogCancellationAudit(command.OrderNumber, callerIdentity);
        return CancelOrderResult.Success;
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
