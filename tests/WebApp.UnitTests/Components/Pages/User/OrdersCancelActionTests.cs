namespace eShop.WebApp.UnitTests.Components.Pages.User;

// REQ-006
[TestClass]
public class OrdersCancelActionTests : BunitContext
{
    [TestMethod("REQ-006 Cancel action is visible only for orders with status Submitted or AwaitingValidation")]
    [DataRow("Submitted", true)]
    [DataRow("AwaitingValidation", true)]
    [DataRow("StockConfirmed", false)]
    [DataRow("Paid", false)]
    [DataRow("Shipped", false)]
    [DataRow("Cancelled", false)]
    public void CancelAction_VisibleOnlyForEligibleStatuses(string status, bool expectedVisible)
    {
        ArrangePage([new OrderRecord(1, DateTime.UtcNow, status, 42.00m)]);

        var cut = Render<Orders>();

        var cancelButtons = cut.FindAll("button[type='submit']").Where(b => b.TextContent.Trim() == "Cancel order");
        Assert.AreEqual(expectedVisible, cancelButtons.Any());
    }

    [TestMethod("REQ-006 The Cancel order form posts to the cancel-order-confirm handler with the order number")]
    public void CancelOrderForm_PostsToConfirmHandler()
    {
        // Guards the SSR wiring: a mismatch between _handler and @formname, or between the hidden
        // field name and the [SupplyParameterFromForm] property, is a silently dead button.
        ArrangePage([new OrderRecord(5, DateTime.UtcNow, "Submitted", 10.00m)]);

        var cut = Render<Orders>();

        var form = cut.Find(".order-actions form");
        Assert.AreEqual("post", form.GetAttribute("method"));
        Assert.AreEqual("cancel-order-confirm", form.QuerySelector("input[name='_handler']")?.GetAttribute("value"));
        Assert.AreEqual("5", form.QuerySelector($"input[name='{nameof(Orders.ConfirmCancelOrderNumber)}']")?.GetAttribute("value"));
        Assert.IsNotNull(cut.Find("#cancel-order-confirm-form"), "a hidden @formname receiver exists for the handler");
    }

    [TestMethod("REQ-006 The Yes, cancel form posts to the cancel-order handler with the order number")]
    public void ConfirmForm_PostsToCancelHandler()
    {
        ArrangePage([new OrderRecord(6, DateTime.UtcNow, "Submitted", 10.00m)]);

        var cut = Render<Orders>();
        cut.Instance.ConfirmCancelOrderNumber = 6;
        cut.Find("#cancel-order-confirm-form").Submit();

        var form = cut.Find("form.orders-cancel-confirm");
        Assert.AreEqual("post", form.GetAttribute("method"));
        Assert.AreEqual("cancel-order", form.QuerySelector("input[name='_handler']")?.GetAttribute("value"));
        Assert.AreEqual("6", form.QuerySelector($"input[name='{nameof(Orders.CancelOrderNumber)}']")?.GetAttribute("value"));
        Assert.IsNotNull(cut.Find("#cancel-order-form"), "a hidden @formname receiver exists for the handler");
    }

    [TestMethod("REQ-006 Pressing Cancel order shows an inline confirmation before any request is sent")]
    public void CancelAction_ShowsInlineConfirmation_BeforeSending()
    {
        var handler = ArrangePage([new OrderRecord(2, DateTime.UtcNow, "Submitted", 10.00m)]);

        var cut = Render<Orders>();
        // Blazor binds ConfirmCancelOrderNumber from the posted "cancel-order-confirm" form body on the SSR round-trip.
        cut.Instance.ConfirmCancelOrderNumber = 2;
        cut.Find("#cancel-order-confirm-form").Submit();

        var confirm = cut.Find("form.orders-cancel-confirm");
        StringAssert.Contains(confirm.TextContent, "Cancel this order?");
        Assert.IsTrue(confirm.QuerySelectorAll("button[type='submit']").Any(b => b.TextContent.Trim() == "Yes, cancel"));
        Assert.AreEqual(0, handler.CancelRequests, "no cancel request is sent until the customer confirms");
    }

    [TestMethod("REQ-006 A confirmation message is displayed after a successful cancellation")]
    public void SuccessfulCancellation_ShowsConfirmationMessage()
    {
        var handler = ArrangePage([new OrderRecord(2, DateTime.UtcNow, "Submitted", 10.00m)]);

        var cut = Render<Orders>();
        cut.Instance.CancelOrderNumber = 2;
        cut.Find("#cancel-order-form").Submit();

        var message = cut.Find("[role='status'].orders-message");
        StringAssert.Contains(message.TextContent, "Order 2 has been cancelled");
        Assert.IsTrue(message.ClassList.Contains("orders-message-success"));
        Assert.AreEqual(1, handler.CancelRequests);
        Assert.AreEqual(2, handler.LastCancelledOrderNumber);
        Assert.IsFalse(string.IsNullOrEmpty(handler.LastRequestId), "the cancel request carries an x-requestid header");
    }

    [TestMethod("REQ-006 The order's displayed status updates to Cancelled without a page reload")]
    public void SuccessfulCancellation_UpdatesStatusWithoutReload()
    {
        ArrangePage([new OrderRecord(3, DateTime.UtcNow, "AwaitingValidation", 15.00m)]);

        var cut = Render<Orders>();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var initialUri = navigationManager.Uri;

        cut.Instance.CancelOrderNumber = 3;
        cut.Find("#cancel-order-form").Submit();

        Assert.AreEqual("Cancelled", cut.Find(".order-status .status").TextContent.Trim());
        Assert.AreEqual(initialUri, navigationManager.Uri);
        Assert.IsFalse(cut.FindAll("button[type='submit']").Any(b => b.TextContent.Trim() == "Cancel order"), "a cancelled order offers no further cancel action");
    }

    [TestMethod("REQ-006 A rejected cancellation shows a failure message and leaves the status unchanged")]
    public void RejectedCancellation_ShowsFailureMessage()
    {
        ArrangePage([new OrderRecord(4, DateTime.UtcNow, "Submitted", 15.00m)], HttpStatusCode.Conflict);

        var cut = Render<Orders>();
        cut.Instance.CancelOrderNumber = 4;
        cut.Find("#cancel-order-form").Submit();

        var message = cut.Find("[role='alert'].orders-message");
        Assert.IsTrue(message.ClassList.Contains("orders-message-error"));
        Assert.AreEqual("Submitted", cut.Find(".order-status .status").TextContent.Trim());
    }

    private FakeOrderingHttpMessageHandler ArrangePage(OrderRecord[] orders, HttpStatusCode cancelStatusCode = HttpStatusCode.OK)
    {
        var handler = new FakeOrderingHttpMessageHandler(orders, cancelStatusCode);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new OrderingService(httpClient));
        Services.AddSingleton<AntiforgeryStateProvider, NullAntiforgeryStateProvider>();
        ComponentFactories.AddStub<OrdersRefreshOnStatusChange>();
        return handler;
    }

    private sealed class NullAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }

    private sealed class FakeOrderingHttpMessageHandler(OrderRecord[] orders, HttpStatusCode cancelStatusCode) : HttpMessageHandler
    {
        public int CancelRequests { get; private set; }

        public int? LastCancelledOrderNumber { get; private set; }

        public string? LastRequestId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Put)
            {
                CancelRequests++;
                LastRequestId = request.Headers.TryGetValues("x-requestid", out var ids) ? ids.FirstOrDefault() : null;
                var body = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
                LastCancelledOrderNumber = body.GetProperty("orderNumber").GetInt32();
                return new HttpResponseMessage(cancelStatusCode);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(orders) };
        }
    }
}
