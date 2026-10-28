using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Chat;
using MadeInMinas.Api.DTOs.ChatOrders;
using MadeInMinas.Api.DTOs.Delivery;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class ChatOrderTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private Product burger = null!;
    private Product fries = null!;
    private ChatConversation conversation = null!;
    private string token = null!;
    private const string ReviewPath = "/api/public-chat/order/review";
    private const string ConfirmPath = "/api/public-chat/order/confirm";

    private static async Task ClearAsync(AppDbContext database)
    {
        await database.ChatConversations.ExecuteDeleteAsync();
        await database.StoreAttendanceSettings.ExecuteDeleteAsync();
        await database.DeliverySettings.ExecuteDeleteAsync();
        await database.StockMovements.ExecuteDeleteAsync();
        await database.PrintJobs.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    }

    public async Task InitializeAsync()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            await ClearAsync(database);
            var category = new Category { Name = "Cardápio", NormalizedName = "CARDÁPIO" };
            burger = new Product { Name = "Lanche teste", NormalizedName = "LANCHE TESTE", Price = 25.90m, IsActive = true, IsAvailable = true, Category = category };
            fries = new Product { Name = "Batata teste", NormalizedName = "BATATA TESTE", Price = 12.50m, IsActive = true, IsAvailable = true, Category = category };
            database.Products.AddRange(burger, fries);
            OrderStockFixture.AddRecipe(database, burger);
            OrderStockFixture.AddRecipe(database, fries);
            conversation = NewConversation();
            AddConversation(database, conversation);
            await database.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        token = scope.ServiceProvider.GetRequiredService<PublicChatAccess>().Issue(conversation.Id, conversation.CreatedAt)!.Token;
    }

    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private static ChatConversation NewConversation() => new()
    {
        VisitorName = "Visitante teste",
        RequestId = Guid.NewGuid(),
        RequestHash = new string('A', 64),
        AutomationMode = "Automatic",
        AiLeaseId = Guid.NewGuid(),
        AiLeaseSourceSequence = 1,
        AiLeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static void AddConversation(AppDbContext database, ChatConversation chat)
    {
        database.ChatConversations.Add(chat);
        database.ChatMessages.Add(new ChatMessage
        {
            ConversationId = chat.Id,
            Sequence = 1,
            Kind = "Visitor",
            RequestId = chat.RequestId,
            Text = "Quero fazer um pedido",
            CreatedAt = chat.CreatedAt
        });
    }

    private async Task<T> WithOrdersAsync<T>(Func<ChatOrderService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ChatOrderService>());
    }

    private Task<ChatOrderStateResponse> SetItemAsync(Guid product, int quantity = 1, string? notes = null, bool existing = false) =>
        WithOrdersAsync(service => service.SetItemAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, product, quantity, notes, CancellationToken.None, existing));

    private Task<ChatOrderStateResponse> DetailsAsync(ChatOrderDetailsRequest request) =>
        WithOrdersAsync(service => service.SetDetailsAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, request, CancellationToken.None));

    private async Task<ChatOrderStateResponse> ReadyAsync(bool finishTurn = true)
    {
        await SetItemAsync(burger.Id, 2, "Sem cebola");
        await DetailsAsync(new(Phone: "(31) 99999-1234", Fulfillment: "Pickup") { PaymentPreference = "Pix" });
        var state = await WithOrdersAsync(service => service.ReviewForToolAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, CancellationToken.None));
        if (finishTurn)
            await FinishTurnAsync();
        return state;
    }

    private Task FinishTurnAsync() => factory.WithDatabaseAsync(async database =>
        await database.ChatConversations.Where(row => row.Id == conversation.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.AiProcessedSequence, 1)
            .SetProperty(row => row.AiLeaseId, (Guid?)null)
            .SetProperty(row => row.AiLeaseExpiresAt, (DateTimeOffset?)null)
            .SetProperty(row => row.AiLeaseSourceSequence, (int?)null)));

    private Task ResumeLeaseAsync() => factory.WithDatabaseAsync(async database =>
        await database.ChatConversations.Where(row => row.Id == conversation.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.AiLeaseId, conversation.AiLeaseId)
            .SetProperty(row => row.AiLeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(5))
            .SetProperty(row => row.AiLeaseSourceSequence, 1)));

    private HttpClient VisitorClient(string? credential = null)
    {
        var client = factory.CreateStaffClient();
        client.DefaultRequestHeaders.Add("X-Chat-Access", credential ?? token);
        return client;
    }

    [Fact]
    public async Task ChangingPaymentInvalidatesPreviousConfirmationAndPersistsChangeRequest()
    {
        var previous = await ReadyAsync(finishTurn: false);
        var changed = await DetailsAsync(new ChatOrderDetailsRequest { PaymentPreference = "Cash", ChangeFor = 100m });
        Assert.Null(changed.Review);
        Assert.True(changed.DraftVersion > previous.DraftVersion);
        var current = await WithOrdersAsync(service => service.ReviewForToolAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, CancellationToken.None));
        Assert.Equal("Cash", current.Review!.PaymentPreference);
        Assert.Equal(100m - current.Review.Total, current.Review.RequestedChange);
        await FinishTurnAsync();
        using var guest = VisitorClient();
        await AssertProblemAsync(await guest.PostAsJsonAsync(ConfirmPath, Confirmation(previous)), "ChatDraftChanged");
        (await guest.PostAsJsonAsync(ConfirmPath, Confirmation(current))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
        {
            var order = await database.Orders.SingleAsync();
            Assert.Equal("Cash", order.PaymentPreference);
            Assert.Equal(100m, order.ChangeFor);
            Assert.False(await database.Payments.AnyAsync());
        });
    }

    private static ChatOrderConfirmationRequest Confirmation(ChatOrderStateResponse state) =>
        new(Guid.NewGuid(), state.DraftVersion, state.Review!.ReviewToken);

    private async Task<HttpClient> StaffClientAsync(int role = 2)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    [Fact]
    public async Task StaffSeesDraftThenConfirmedOrderAndItsCurrentStatusWithoutCreatingAgain()
    {
        var state = await ReadyAsync();
        using var staff = await StaffClientAsync();
        var before = (await staff.GetFromJsonAsync<StaffChatResponse>($"/api/chat/{conversation.Id}"))!;
        Assert.Null(before.LinkedOrder);
        Assert.NotNull(before.Transcript.Order!.Review);
        using var visitor = VisitorClient();
        (await visitor.PostAsJsonAsync(ConfirmPath, Confirmation(state))).EnsureSuccessStatusCode();
        var received = (await staff.GetFromJsonAsync<StaffChatResponse>($"/api/chat/{conversation.Id}?after=1"))!;
        Assert.NotNull(received.LinkedOrder);
        Assert.Equal("New", received.LinkedOrder.Status);
        Assert.Equal("Pickup", received.LinkedOrder.Fulfillment);
        Assert.Equal(51.80m, received.LinkedOrder.Total);
        Assert.Equal(0m, received.LinkedOrder.DeliveryFee);
        var accepted = await staff.PutAsJsonAsync($"/api/orders/{received.LinkedOrder.Id}/status", new OrderStatusRequest("Confirmed", 1));
        accepted.EnsureSuccessStatusCode();
        var refreshed = (await staff.GetFromJsonAsync<StaffChatResponse>($"/api/chat/{conversation.Id}?after=1"))!;
        Assert.Equal(received.LinkedOrder.Id, refreshed.LinkedOrder!.Id);
        Assert.Equal("Confirmed", refreshed.LinkedOrder.Status);
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task StaffShortcutRejectsAMismatchedConfirmationRequest()
    {
        using var visitor = VisitorClient();
        (await visitor.PostAsJsonAsync(ConfirmPath, Confirmation(await ReadyAsync()))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
            await database.ChatOrderDrafts.Where(draft => draft.ConversationId == conversation.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(draft => draft.OrderRequestId, Guid.NewGuid())));
        using var staff = await StaffClientAsync();
        var response = (await staff.GetFromJsonAsync<StaffChatResponse>($"/api/chat/{conversation.Id}"))!;
        Assert.Null(response.LinkedOrder);
    }

    [Fact]
    public async Task AnOrderNumberMentionedInAnotherConversationDoesNotCreateAStaffShortcut()
    {
        using var visitor = VisitorClient();
        (await visitor.PostAsJsonAsync(ConfirmPath, Confirmation(await ReadyAsync()))).EnsureSuccessStatusCode();
        var other = NewConversation();
        await factory.WithDatabaseAsync(async database =>
        {
            AddConversation(database, other);
            var message = database.ChangeTracker.Entries<ChatMessage>().Single().Entity;
            message.Text = $"Quero consultar o pedido #{(await database.Orders.SingleAsync()).Number}";
            await database.SaveChangesAsync();
        });
        using var staff = await StaffClientAsync();
        var response = (await staff.GetFromJsonAsync<StaffChatResponse>($"/api/chat/{other.Id}"))!;
        Assert.Null(response.LinkedOrder);
        Assert.Null(response.Transcript.Order);
    }

    [Fact]
    public async Task PublicTranscriptKeepsStaffOrderIdentifiersPrivate()
    {
        using var visitor = VisitorClient();
        (await visitor.PostAsJsonAsync(ConfirmPath, Confirmation(await ReadyAsync()))).EnsureSuccessStatusCode();
        var response = await visitor.GetAsync("/api/public-chat");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.TryGetProperty("linkedOrder", out _));
        await factory.WithDatabaseAsync(async database => Assert.DoesNotContain((await database.Orders.SingleAsync()).Id.ToString(), body));
        var unauthorized = await visitor.GetAsync($"/api/chat/{conversation.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task KitchenAndDispatchCannotReadChatOrderShortcut(int role)
    {
        using var visitor = VisitorClient();
        (await visitor.PostAsJsonAsync(ConfirmPath, Confirmation(await ReadyAsync()))).EnsureSuccessStatusCode();
        using var staff = await StaffClientAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/chat/{conversation.Id}")).StatusCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.Conflict)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddChangeAndRemoveUseServerAmountsAndIdempotentTargetQuantities()
    {
        var first = await SetItemAsync(burger.Id, 2, "Sem cebola");
        Assert.Equal(51.80m, first.Quote!.Subtotal);
        var repeat = await SetItemAsync(burger.Id, 2, "Sem cebola");
        Assert.Equal(first.DraftVersion, repeat.DraftVersion);
        var changed = await SetItemAsync(burger.Id, 3, existing: true);
        Assert.Equal(3, Assert.Single(changed.Items).Quantity);
        Assert.Equal("Sem cebola", changed.Items[0].Notes);
        Assert.Equal(77.70m, changed.Quote!.Subtotal);
        await SetItemAsync(fries.Id);
        var removed = await WithOrdersAsync(service => service.RemoveItemAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, burger.Id, CancellationToken.None));
        Assert.Equal(fries.Id, Assert.Single(removed.Items).ProductId);
        Assert.Equal(12.50m, removed.Quote!.Subtotal);
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task ChangingAnAbsentItemCannotInsertIt()
    {
        await SetItemAsync(burger.Id);
        await Assert.ThrowsAsync<ChatException>(() => SetItemAsync(fries.Id, 2, existing: true));
        var state = await WithOrdersAsync(service => service.GetStateAsync(conversation.Id, CancellationToken.None));
        Assert.Equal(burger.Id, Assert.Single(state!.Items).ProductId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100)]
    public async Task InvalidToolQuantityIsRejectedBeforeDraftWrites(int quantity)
    {
        await Assert.ThrowsAsync<ChatException>(() => SetItemAsync(burger.Id, quantity));
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Set<ChatOrderDraft>().CountAsync()));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("notes")]
    [InlineData("address")]
    public async Task ConstructorAnnotationsAndNestedContractsRejectOversizedToolFields(string field)
    {
        var original = await SetItemAsync(burger.Id);
        var request = field switch
        {
            "name" => new ChatOrderDetailsRequest(Name: new string('N', 121)),
            "notes" => new ChatOrderDetailsRequest(Notes: new string('N', 501)),
            _ => new ChatOrderDetailsRequest(Fulfillment: "Delivery", Address: new PublicDeliveryAddressRequest("corinto", new string('R', 121), "10"))
        };
        await Assert.ThrowsAsync<ChatException>(() => DetailsAsync(request));
        var state = await WithOrdersAsync(service => service.GetStateAsync(conversation.Id, CancellationToken.None));
        Assert.Equal(original.DraftVersion, state!.DraftVersion);
        Assert.Equal(original.Name, state.Name);
        Assert.Null(state.Address);
        Assert.Null(state.Notes);
    }

    [Fact]
    public async Task MissingRequiredQuantityAndEmptyItemsFailInternalContractValidation()
    {
        Assert.Throws<ChatException>(() => ChatOrderService.ValidateInput(new MadeInMinas.Api.DTOs.Cart.CartItemRequest(burger.Id, null)));
        Assert.Throws<ChatException>(() => ChatOrderService.ValidateInput(new MadeInMinas.Api.DTOs.PublicCart.PublicCartQuoteRequest([])));
        await Assert.ThrowsAsync<ChatException>(() => SetItemAsync(burger.Id, 1, new string('N', 251)));
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Set<ChatOrderDraft>().CountAsync()));
    }

    [Fact]
    public async Task MissingDataPreventsReviewAndOrderCreation()
    {
        var draft = await SetItemAsync(burger.Id);
        Assert.Contains("phone", draft.MissingFields);
        Assert.Contains("fulfillment", draft.MissingFields);
        await FinishTurnAsync();
        using var client = VisitorClient();
        await AssertProblemAsync(await client.PostAsJsonAsync(ReviewPath, new ChatOrderReviewRequest(draft.DraftVersion)), "ChatOrderIncomplete");
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, new ChatOrderConfirmationRequest(Guid.NewGuid(), draft.DraftVersion, new string('A', 64))), "ChatOrderReviewRequired");
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task ReviewDoesNotCreateAnOrderAndConfirmationUsesExistingCheckoutRules()
    {
        var state = await ReadyAsync();
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Orders.CountAsync()));
        using var client = VisitorClient();
        var response = await client.PostAsJsonAsync(ConfirmPath, Confirmation(state));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var confirmed = (await response.Content.ReadFromJsonAsync<ChatOrderStateResponse>())!;
        Assert.Equal(51.80m, confirmed.Receipt!.Total);
        Assert.NotNull(confirmed.Receipt.Tracking);
        await factory.WithDatabaseAsync(async database =>
        {
            var order = await database.Orders.SingleAsync();
            var draft = await database.Set<ChatOrderDraft>().SingleAsync();
            Assert.Equal(order.Id, draft.OrderId);
            Assert.Equal("New", order.Status);
            Assert.Equal("Pending", order.StockStatus);
            Assert.Equal("DirectLink", order.Origin);
            Assert.Null(order.CreatedById);
            Assert.Equal(0, await database.Payments.CountAsync());
            Assert.Equal(0, await database.StockMovements.CountAsync());
        });
    }

    [Fact]
    public async Task ConcurrentConfirmationRetriesProduceOnlyOneOrder()
    {
        var state = await ReadyAsync();
        var request = Confirmation(state);
        using var first = VisitorClient();
        using var second = VisitorClient();
        var responses = await Task.WhenAll(first.PostAsJsonAsync(ConfirmPath, request), second.PostAsJsonAsync(ConfirmPath, request));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var numbers = await Task.WhenAll(responses.Select(async response => (await response.Content.ReadFromJsonAsync<ChatOrderStateResponse>())!.Receipt!.Number));
        Assert.Equal(numbers[0], numbers[1]);
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task DraftChangeInvalidatesReviewAndRejectsPreviousConfirmation()
    {
        var reviewed = await ReadyAsync();
        await ResumeLeaseAsync();
        var changed = await SetItemAsync(burger.Id, 3, existing: true);
        Assert.Null(changed.Review);
        Assert.True(changed.NeedsReview);
        using var client = VisitorClient();
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, Confirmation(reviewed)), "ChatDraftChanged");
        await FinishTurnAsync();
        var rereview = await client.PostAsJsonAsync(ReviewPath, new ChatOrderReviewRequest(changed.DraftVersion));
        var state = (await rereview.Content.ReadFromJsonAsync<ChatOrderStateResponse>())!;
        Assert.Equal(77.70m, state.Review!.Total);
        Assert.NotEqual(reviewed.Review!.ReviewToken, state.Review.ReviewToken);
    }

    [Fact]
    public async Task NewVisitorMessageInvalidatesReviewAndRevokesOldToolLease()
    {
        var reviewed = await ReadyAsync();
        using var client = VisitorClient();
        (await client.PostAsJsonAsync("/api/public-chat/messages", new SendChatMessageRequest(Guid.NewGuid(), "Mude meu pedido"))).EnsureSuccessStatusCode();
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, Confirmation(reviewed)), "ChatDraftChanged");
        await Assert.ThrowsAsync<ChatException>(() => SetItemAsync(burger.Id, 3));
        var state = await WithOrdersAsync(service => service.GetStateAsync(conversation.Id, CancellationToken.None));
        Assert.Null(state!.Review);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("unavailable")]
    public async Task CommercialChangeAfterReviewRejectsCreationAtomically(string change)
    {
        var state = await ReadyAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            var product = await database.Products.SingleAsync(product => product.Id == burger.Id);
            if (change == "price")
                product.Price++;
            else
                product.IsAvailable = false;
            await database.SaveChangesAsync();
        });
        using var client = VisitorClient();
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, Confirmation(state)), change == "price" ? "OrderReviewChanged" : "CartProductUnavailable");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(0, await database.Orders.CountAsync());
            Assert.Equal(0, await database.Customers.CountAsync());
            Assert.Null((await database.Set<ChatOrderDraft>().SingleAsync()).ReceiptJson);
        });
    }

    [Fact]
    public async Task OtherConversationAndForgedCredentialCannotConfirmThisDraft()
    {
        var state = await ReadyAsync();
        var other = NewConversation();
        await factory.WithDatabaseAsync(async database => { AddConversation(database, other); await database.SaveChangesAsync(); });
        await using var scope = factory.Services.CreateAsyncScope();
        var otherToken = scope.ServiceProvider.GetRequiredService<PublicChatAccess>().Issue(other.Id, other.CreatedAt)!.Token;
        using var otherClient = VisitorClient(otherToken);
        await AssertProblemAsync(await otherClient.PostAsJsonAsync(ConfirmPath, Confirmation(state)), "ChatOrderReviewRequired");
        using var forged = VisitorClient("not-a-capability");
        await AssertProblemAsync(await forged.PostAsJsonAsync(ConfirmPath, Confirmation(state)), "ChatUnavailable", HttpStatusCode.NotFound);
        var transcript = await otherClient.GetFromJsonAsync<ChatTranscriptResponse>("/api/public-chat");
        Assert.Null(transcript!.Order);
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task PriceAndDiscountFieldsAreRejectedByThePublicConfirmationContract()
    {
        var state = await ReadyAsync();
        using var client = VisitorClient();
        var response = await client.PostAsJsonAsync(ConfirmPath, new
        {
            requestId = Guid.NewGuid(),
            expectedDraftVersion = state.DraftVersion,
            reviewToken = state.Review!.ReviewToken,
            price = 0.01m,
            discount = 999m
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task FailedToolCannotChangeDraftOrReturnAdministrativeInformation()
    {
        var original = await SetItemAsync(burger.Id);
        await Assert.ThrowsAsync<CartException>(() => SetItemAsync(Guid.NewGuid()));
        var state = await WithOrdersAsync(service => service.GetStateAsync(conversation.Id, CancellationToken.None));
        Assert.Equal(original.DraftVersion, state!.DraftVersion);
        Assert.Equal(burger.Id, Assert.Single(state.Items).ProductId);
        var json = JsonSerializer.Serialize(state).ToLowerInvariant();
        foreach (var forbidden in new[] { "cost", "margin", "currentstock", "recipe", "cmv", "profit", "securitystamp" })
            Assert.DoesNotContain(forbidden, json);
    }

    [Fact]
    public async Task ReusedScopeStillObservesRevokedLeaseFromAnotherInstance()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ChatOrderService>();
        await service.SetItemAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, burger.Id, 1, null, CancellationToken.None);
        await factory.WithDatabaseAsync(async database =>
        {
            await database.ChatConversations.Where(row => row.Id == conversation.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AiLeaseId, (Guid?)null)
                    .SetProperty(row => row.AiLeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(row => row.AiLeaseSourceSequence, (int?)null));
        });
        await Assert.ThrowsAsync<ChatException>(() => service.SetItemAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, burger.Id, 2, null, CancellationToken.None));
        var state = await service.GetStateAsync(conversation.Id, CancellationToken.None);
        Assert.Equal(1, Assert.Single(state!.Items).Quantity);
    }

    [Fact]
    public async Task ExpiredLeasePreventsToolMutation()
    {
        await factory.WithDatabaseAsync(async database => await database.ChatConversations.Where(row => row.Id == conversation.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AiLeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Assert.ThrowsAsync<ChatException>(() => SetItemAsync(burger.Id));
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Set<ChatOrderDraft>().CountAsync()));
    }

    [Fact]
    public async Task DeliveryQuoteAndConfirmationUseConfiguredAreaFeeAndActualNeighborhood()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var area = new DeliveryAreaResponse("corinto", "Todos os bairros", "Corinto", "MG", 5m, true) { CoversAllNeighborhoods = true };
            database.DeliverySettings.Add(new DeliverySettings { Version = 1, AreasJson = JsonSerializer.Serialize(new[] { area }) });
            await database.SaveChangesAsync();
        });
        await SetItemAsync(burger.Id);
        await DetailsAsync(new(Phone: "31999991234", Fulfillment: "Delivery",
            Address: new PublicDeliveryAddressRequest("corinto", "Rua teste", "10") { Neighborhood = "Bairro informado" })
        { PaymentPreference = "Pix" });
        var state = await WithOrdersAsync(service => service.ReviewForToolAsync(conversation.Id, conversation.AiLeaseId!.Value, 1, CancellationToken.None));
        await FinishTurnAsync();
        Assert.Equal(5m, state.Review!.DeliveryFee);
        Assert.Equal(30.90m, state.Review.Total);
        Assert.Equal("Bairro informado", state.Review.Address!.Neighborhood);
        using var client = VisitorClient();
        (await client.PostAsJsonAsync(ConfirmPath, Confirmation(state))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
        {
            var order = await database.Orders.SingleAsync();
            Assert.Equal(5m, order.DeliveryFee);
            Assert.Equal("Bairro informado", order.AddressNeighborhood);
            Assert.Equal("Corinto", order.AddressCity);
            Assert.Null(order.AddressId);
        });
    }

    [Fact]
    public async Task ConfirmationReceiptSurvivesPauseAndRejectsAChangedRetry()
    {
        var state = await ReadyAsync();
        var request = Confirmation(state);
        using var client = VisitorClient();
        var first = await client.PostAsJsonAsync(ConfirmPath, request);
        first.EnsureSuccessStatusCode();
        var receipt = (await first.Content.ReadFromJsonAsync<ChatOrderStateResponse>())!.Receipt!;
        await factory.WithDatabaseAsync(async database =>
        {
            var settings = await database.StoreAttendanceSettings.SingleOrDefaultAsync();
            var defaults = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StoreAttendanceOptions>>().Value.Data();
            if (settings is null)
            {
                settings = new StoreAttendanceSettings { Version = 1 };
                database.StoreAttendanceSettings.Add(settings);
            }
            settings.SettingsJson = JsonSerializer.Serialize(defaults with { IsPaused = true });
            await database.SaveChangesAsync();
        });
        var repeated = await client.PostAsJsonAsync(ConfirmPath, request);
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(receipt.Number, (await repeated.Content.ReadFromJsonAsync<ChatOrderStateResponse>())!.Receipt!.Number);
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, request with { RequestId = Guid.NewGuid() }), "ChatRequestConflict");
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Orders.CountAsync()));
    }

    [Fact]
    public async Task PublicReviewAndConfirmationWaitForTheCurrentAutomaticTurn()
    {
        var state = await ReadyAsync(finishTurn: false);
        using var client = VisitorClient();
        await AssertProblemAsync(await client.PostAsJsonAsync(ReviewPath, new ChatOrderReviewRequest(state.DraftVersion)), "ChatAutomationPending");
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, Confirmation(state)), "ChatAutomationPending");
        await factory.WithDatabaseAsync(async database => Assert.Equal(0, await database.Orders.CountAsync()));
        await FinishTurnAsync();
        (await client.PostAsJsonAsync(ConfirmPath, Confirmation(state))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, (await database.ChatConversations.SingleAsync()).AiProcessedSequence));
    }

    [Fact]
    public async Task ReceivedOrderRejectsLaterDraftChangesAndKeepsItsOriginalReceipt()
    {
        var reviewed = await ReadyAsync();
        var confirmation = Confirmation(reviewed);
        using var client = VisitorClient();
        var confirmedResponse = await client.PostAsJsonAsync(ConfirmPath, confirmation);
        confirmedResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmedResponse.Content.ReadFromJsonAsync<ChatOrderStateResponse>())!;
        Assert.NotNull(confirmed.Receipt);

        (await client.PostAsJsonAsync("/api/public-chat/messages", new SendChatMessageRequest(Guid.NewGuid(), "Quero alterar o pedido que já enviei"))).EnsureSuccessStatusCode();
        var nextLease = Guid.NewGuid();
        var source = 0;
        await factory.WithDatabaseAsync(async database =>
        {
            source = await database.ChatMessages.Where(message => message.ConversationId == conversation.Id && message.Kind == "Visitor")
                .MaxAsync(message => message.Sequence);
            await database.ChatConversations.Where(row => row.Id == conversation.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.AutomationMode, "Automatic")
                .SetProperty(row => row.AiLeaseId, nextLease)
                .SetProperty(row => row.AiLeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(5))
                .SetProperty(row => row.AiLeaseSourceSequence, source));
        });

        var itemFailure = await Assert.ThrowsAsync<ChatException>(() => WithOrdersAsync(service => service.SetItemAsync(
            conversation.Id, nextLease, source, burger.Id, 3, null, CancellationToken.None)));
        Assert.Equal(ChatError.ChatOrderAlreadyCreated, itemFailure.Error);
        var detailsFailure = await Assert.ThrowsAsync<ChatException>(() => WithOrdersAsync(service => service.SetDetailsAsync(
            conversation.Id, nextLease, source, new ChatOrderDetailsRequest(Name: "Outro nome", Notes: "Alteração posterior"), CancellationToken.None)));
        Assert.Equal(ChatError.ChatOrderAlreadyCreated, detailsFailure.Error);
        var removalFailure = await Assert.ThrowsAsync<ChatException>(() => WithOrdersAsync(service => service.RemoveItemAsync(
            conversation.Id, nextLease, source, burger.Id, CancellationToken.None)));
        Assert.Equal(ChatError.ChatOrderAlreadyCreated, removalFailure.Error);
        var reviewFailure = await Assert.ThrowsAsync<ChatException>(() => WithOrdersAsync(service => service.ReviewForToolAsync(
            conversation.Id, nextLease, source, CancellationToken.None)));
        Assert.Equal(ChatError.ChatOrderAlreadyCreated, reviewFailure.Error);
        await AssertProblemAsync(await client.PostAsJsonAsync(ReviewPath, new ChatOrderReviewRequest(confirmed.DraftVersion)), "ChatOrderAlreadyCreated");
        await AssertProblemAsync(await client.PostAsJsonAsync(ConfirmPath, confirmation with { RequestId = Guid.NewGuid() }), "ChatRequestConflict");

        var preserved = await WithOrdersAsync(service => service.GetStateAsync(conversation.Id, CancellationToken.None));
        Assert.Equal(confirmed.DraftVersion, preserved!.DraftVersion);
        Assert.Equal(confirmed.Name, preserved.Name);
        Assert.Equal(confirmed.Notes, preserved.Notes);
        Assert.Equal(2, Assert.Single(preserved.Items).Quantity);
        Assert.Equal(confirmed.Receipt!.Number, preserved.Receipt!.Number);
        Assert.Equal(confirmed.Receipt.Total, preserved.Receipt.Total);
        await factory.WithDatabaseAsync(async database =>
        {
            var order = Assert.Single(await database.Orders.Include(order => order.Items).ToArrayAsync());
            Assert.Equal(2, Assert.Single(order.Items).Quantity);
            Assert.Equal(confirmed.Receipt.Number, order.Number);
            Assert.Equal(confirmed.Receipt.Total, order.Total);
        });
    }
}
