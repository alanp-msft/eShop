namespace eShop.Catalog.UnitTests.IntegrationEvents.EventHandling;

// REQ-004
[TestClass]
public class OrderStatusChangedToCancelledIntegrationEventHandlerTest
{
    private static CatalogContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new InMemoryCatalogContext(options);
    }

    /// <summary>
    /// The production model maps <see cref="CatalogItem.Embedding"/> to a pgvector column, which the
    /// in-memory provider cannot represent; ignoring it here leaves AvailableStock and the rest of the
    /// model exactly as Catalog.API configures them.
    /// </summary>
    private sealed class InMemoryCatalogContext : CatalogContext
    {
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public InMemoryCatalogContext(DbContextOptions<CatalogContext> options)
            : base(options, Substitute.For<IConfiguration>())
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<CatalogItem>().Ignore(ci => ci.Embedding);
        }
    }

    [TestMethod("REQ-004 Handling OrderStatusChangedToCancelledIntegrationEvent does not change AvailableStock for any catalog item")]
    public async Task Handle_DoesNotChangeAvailableStock_ForAnyCatalogItem()
    {
        await using var context = CreateContext(nameof(Handle_DoesNotChangeAvailableStock_ForAnyCatalogItem));

        context.Set<CatalogItem>().AddRange(
            new CatalogItem("Item one") { AvailableStock = 10, RestockThreshold = 1, MaxStockThreshold = 100, Price = 9.99m },
            new CatalogItem("Item two") { AvailableStock = 0, RestockThreshold = 1, MaxStockThreshold = 100, Price = 4.99m },
            new CatalogItem("Item three") { AvailableStock = 42, RestockThreshold = 5, MaxStockThreshold = 200, Price = 19.99m });
        await context.SaveChangesAsync();

        var expectedStockByItemId = await context.Set<CatalogItem>()
            .AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.AvailableStock);

        var logger = Substitute.For<ILogger<OrderStatusChangedToCancelledIntegrationEventHandler>>();
        var handler = new OrderStatusChangedToCancelledIntegrationEventHandler(logger);
        var @event = new OrderStatusChangedToCancelledIntegrationEvent(
            orderId: 1,
            orderStatus: OrderStatus.Cancelled,
            buyerName: "Test Buyer",
            buyerIdentityGuid: Guid.NewGuid().ToString());

        await handler.Handle(@event);

        var actualStockByItemId = await context.Set<CatalogItem>()
            .AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.AvailableStock);

        Assert.AreEqual(expectedStockByItemId.Count, actualStockByItemId.Count);
        foreach (var (itemId, expectedStock) in expectedStockByItemId)
        {
            Assert.AreEqual(expectedStock, actualStockByItemId[itemId], $"AvailableStock changed for catalog item {itemId}.");
        }
    }
}
