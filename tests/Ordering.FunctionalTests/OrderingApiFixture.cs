using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using eShop.IntegrationEventLogEF;
using eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.Infrastructure;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

using System.Text.Json;
using System.Threading;

namespace eShop.Ordering.FunctionalTests;

public sealed class OrderingApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // REQ-002 (Assumption A3): second buyer identity used to prove ownership enforcement against a
    // real, non-owning buyer rather than a merely-absent buyer.
    public const string OtherBuyerIdentityGuid = "11111111-1111-1111-1111-111111111111";

    private readonly IHost _app;
    private readonly SemaphoreSlim _seedGate = new(1, 1);

    public IResourceBuilder<PostgresServerResource> Postgres { get; private set; }
    public IResourceBuilder<PostgresServerResource> IdentityDB { get; private set; }
    public IResourceBuilder<ProjectResource> IdentityApi { get; private set; }

    private string _postgresConnectionString;

    public OrderingApiFixture()
    {
        var options = new DistributedApplicationOptions { AssemblyName = typeof(OrderingApiFixture).Assembly.FullName, DisableDashboard = true };
        var appBuilder = DistributedApplication.CreateBuilder(options);
        Postgres = appBuilder.AddPostgres("OrderingDB");
        IdentityDB = appBuilder.AddPostgres("IdentityDB");
        IdentityApi = appBuilder.AddProject<Projects.Identity_API>("identity-api").WithReference(IdentityDB);
        _app = appBuilder.Build();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string>
            {
                { $"ConnectionStrings:{Postgres.Resource.Name}", _postgresConnectionString },
                { "Identity:Url", IdentityApi.GetEndpoint("http").Url }
            });
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new AutoAuthorizeStartupFilter());
        });
        return base.CreateHost(builder);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _app.StopAsync();
        if (_app is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _app.Dispose();
        }
    }

    public async ValueTask InitializeAsync()
    {
        await _app.StartAsync();
        _postgresConnectionString = await Postgres.Resource.GetConnectionStringAsync();
    }

    private class AutoAuthorizeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                builder.UseMiddleware<AutoAuthorizeMiddleware>();
                next(builder);
            };
        }
    }

    // REQ-002, REQ-005, REQ-007 (Assumption A3): idempotently seeds the owning buyer
    // (IdentityGuid = AutoAuthorizeMiddleware.IDENTITY_ID, matching the fixed test identity injected
    // by AutoAuthorizeMiddleware) and a second buyer with a different IdentityGuid, through the API's
    // own OrderingContext, using the Buyer/Order constructors (no raw SQL). Buyers are reused across
    // calls (looked up by IdentityGuid before inserting); a fresh Submitted order is created for each
    // buyer on every call so tests that mutate order status do not interfere with one another.
    public async Task<(int OwnerOrderId, int OtherBuyerOrderId)> SeedOwnerAndOtherBuyerOrdersAsync(CancellationToken cancellationToken)
    {
        await _seedGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();

            var ownerBuyer = await GetOrCreateBuyerAsync(context, AutoAuthorizeMiddleware.IDENTITY_ID, "Owner Buyer", cancellationToken);
            var otherBuyer = await GetOrCreateBuyerAsync(context, OtherBuyerIdentityGuid, "Other Buyer", cancellationToken);

            var ownerOrder = NewSubmittedOrder(AutoAuthorizeMiddleware.IDENTITY_ID, ownerBuyer.Id);
            var otherOrder = NewSubmittedOrder(OtherBuyerIdentityGuid, otherBuyer.Id);

            context.Orders.AddRange(ownerOrder, otherOrder);
            await context.SaveChangesAsync(cancellationToken);

            return (ownerOrder.Id, otherOrder.Id);
        }
        finally
        {
            _seedGate.Release();
        }
    }

    // REQ-005: reads the current OrderStatus directly from a fresh OrderingContext scope so tests can
    // assert an order was left unchanged (e.g., after a rejected cancel attempt).
    public async Task<OrderStatus> GetOrderStatusAsync(int orderId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        var order = await context.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        return order.OrderStatus;
    }

    // REQ-005: counts IntegrationEventLogEntry rows for the given order whose EventTypeName ends with
    // OrderStatusChangedToCancelledIntegrationEvent. Matching is done by parsing each entry's Content
    // JSON and comparing the OrderId property, rather than substring-matching the raw (indented) JSON,
    // to avoid false positives/negatives from formatting.
    public async Task<int> CountCancelledIntegrationEventLogEntriesAsync(int orderId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();

        var entries = await context.Set<IntegrationEventLogEntry>()
            .Where(e => e.EventTypeName.EndsWith("OrderStatusChangedToCancelledIntegrationEvent"))
            .ToListAsync(cancellationToken);

        return entries.Count(e => JsonDocument.Parse(e.Content).RootElement.GetProperty("OrderId").GetInt32() == orderId);
    }

    private static async Task<Buyer> GetOrCreateBuyerAsync(OrderingContext context, string identityGuid, string name, CancellationToken cancellationToken)
    {
        var buyer = await context.Buyers.FirstOrDefaultAsync(b => b.IdentityGuid == identityGuid, cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(identityGuid, name);
        context.Buyers.Add(buyer);
        await context.SaveChangesAsync(cancellationToken);
        return buyer;
    }

    private static Order NewSubmittedOrder(string userId, int buyerId)
    {
        var address = new Address("1 Main St", "Redmond", "WA", "USA", "98052");
        return new Order(
            userId,
            "Test User",
            address,
            cardTypeId: 1,
            cardNumber: "1111222233334444",
            cardSecurityNumber: "123",
            cardHolderName: "Test User",
            cardExpiration: DateTime.UtcNow.AddYears(1),
            buyerId: buyerId);
    }
}
