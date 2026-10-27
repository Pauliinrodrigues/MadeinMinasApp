using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.ChatOrders;
using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.DTOs.PublicOrders;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class ChatOrderService(AppDbContext database, PublicCartService cart,
    PublicCheckoutService checkout, PublicOrderTrackingService tracking, PublicChatAccess access, TimeProvider clock)
{
    public Task<ChatOrderStateResponse?> GetStateAsync(Guid id, CancellationToken cancellationToken) =>
        ReadStateAsync(id, cancellationToken);

    public Task<ChatOrderStateResponse> SetItemAsync(Guid id, Guid lease, int source, Guid productId,
        int quantity, string? notes, CancellationToken cancellationToken, bool requireExisting = false) =>
        MutateAsync(id, lease, source, async (conversation, draft) =>
        {
            var items = Read<CartItemRequest[]>(draft.ItemsJson)!;
            var existing = items.SingleOrDefault(line => line.ProductId == productId);
            if (requireExisting && existing is null)
                throw Invalid("Este produto não está no pedido em montagem.");
            var item = new CartItemRequest(productId, quantity, requireExisting && notes is null ? existing?.Notes : notes);
            ValidateInput(item);
            var normalized = item with { Notes = Clean(item.Notes) };
            var updated = items.Any(line => line.ProductId == productId)
                ? items.Select(line => line.ProductId == productId ? normalized : line).ToArray()
                : [.. items, normalized];
            var request = new PublicCartQuoteRequest(updated, draft.Notes);
            ValidateInput(request);
            if (Serialize(updated) == Serialize(items))
                return State(draft);
            var quote = await cart.QuoteAsync(request, cancellationToken);
            draft.ItemsJson = Serialize(updated);
            draft.QuoteJson = Serialize(quote);
            Changed(draft);
            return await SaveAsync(draft, cancellationToken);
        }, cancellationToken);

    public Task<ChatOrderStateResponse> RemoveItemAsync(Guid id, Guid lease, int source, Guid productId,
        CancellationToken cancellationToken) =>
        MutateAsync(id, lease, source, async (conversation, draft) =>
        {
            if (productId == Guid.Empty)
                throw Invalid("Selecione um produto do pedido em montagem.");
            var items = Read<CartItemRequest[]>(draft.ItemsJson)!;
            var updated = items.Where(item => item.ProductId != productId).ToArray();
            if (updated.Length == items.Length)
                return State(draft);
            var quote = updated.Length == 0 ? null
                : await cart.QuoteAsync(new(updated, draft.Notes), cancellationToken);
            draft.ItemsJson = Serialize(updated);
            draft.QuoteJson = quote is null ? null : Serialize(quote);
            Changed(draft);
            return await SaveAsync(draft, cancellationToken);
        }, cancellationToken);

    public Task<ChatOrderStateResponse> SetDetailsAsync(Guid id, Guid lease, int source,
        ChatOrderDetailsRequest request, CancellationToken cancellationToken) =>
        MutateAsync(id, lease, source, async (conversation, draft) =>
        {
            ValidateInput(request);
            var name = request.Name is null ? draft.Name : Clean(request.Name)!;
            var phone = request.Phone is null ? draft.Phone : Clean(request.Phone);
            var fulfillment = request.Fulfillment ?? draft.Fulfillment;
            var address = fulfillment == "Pickup" ? null : request.Address ?? Read<PublicDeliveryAddressRequest>(draft.AddressJson);
            var notes = request.Notes is null ? draft.Notes : Clean(request.Notes);
            var addressJson = address is null ? null : Serialize(address);
            if (name == draft.Name && phone == draft.Phone && fulfillment == draft.Fulfillment
                && Serialize(Read<PublicDeliveryAddressRequest>(draft.AddressJson)) == Serialize(address) && notes == draft.Notes)
                return State(draft);
            var items = Read<CartItemRequest[]>(draft.ItemsJson)!;
            var quote = items.Length == 0 ? null : await cart.QuoteAsync(new(items, notes), cancellationToken);
            draft.Name = name;
            draft.Phone = phone;
            draft.Fulfillment = fulfillment;
            draft.AddressJson = addressJson;
            draft.Notes = notes;
            draft.QuoteJson = quote is null ? null : Serialize(quote);
            Changed(draft);
            return await SaveAsync(draft, cancellationToken);
        }, cancellationToken);

    public Task<ChatOrderStateResponse> ReviewForToolAsync(Guid id, Guid lease, int source,
        CancellationToken cancellationToken) =>
        MutateAsync(id, lease, source, async (conversation, draft) =>
            await ReviewDraftAsync(draft, cancellationToken), cancellationToken);

    public async Task<PublicOrderTrackingResponse?> ReadStatusForToolAsync(Guid id, Guid lease, int source,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await RequireLeaseAsync(id, lease, source, cancellationToken);
        var draft = await database.Set<ChatOrderDraft>().AsNoTracking().SingleOrDefaultAsync(row => row.ConversationId == id, cancellationToken);
        var receipt = Read<PublicOrderReceipt>(draft?.ReceiptJson);
        var response = receipt?.Tracking is null ? null : await tracking.GetAsync(receipt.Tracking.Token, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    // Caller already holds the conversation lock and owns its transaction.
    public async Task InvalidateReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var draft = await database.Set<ChatOrderDraft>().SingleOrDefaultAsync(row => row.ConversationId == id, cancellationToken);
        if (draft is null || draft.ReviewJson is null || draft.ReceiptJson is not null)
            return;
        Changed(draft);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatOrderStateResponse> ReviewPublicAsync(string? token, ChatOrderReviewRequest request,
        CancellationToken cancellationToken)
    {
        ValidateInput(request);
        var id = access.Read(token) ?? throw Unavailable();
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var conversation = await RequireVisitorAsync(id, cancellationToken);
        var draft = await database.Set<ChatOrderDraft>().SingleOrDefaultAsync(row => row.ConversationId == id, cancellationToken)
            ?? throw new ChatException(ChatError.ChatOrderIncomplete, "Monte o pedido na conversa antes de revisar.");
        RequireDraftVersion(draft, request.ExpectedDraftVersion!.Value);
        RequireEditable(draft);
        await RequireCompletedTurnAsync(conversation, cancellationToken);
        var response = await ReviewDraftAsync(draft, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<ChatOrderStateResponse> ConfirmPublicAsync(string? token, ChatOrderConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateInput(request);
        var id = access.Read(token) ?? throw Unavailable();
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var conversation = await LockAsync(id, cancellationToken);
        var draft = await database.Set<ChatOrderDraft>().SingleOrDefaultAsync(row => row.ConversationId == id, cancellationToken)
            ?? throw new ChatException(ChatError.ChatOrderReviewRequired, "Revise o pedido antes de confirmar.");
        var confirmationHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.ExpectedDraftVersion,
            request.ReviewToken
        })));
        // A confirmed attempt can be recovered even if the conversation was subsequently closed.
        if (draft.ReceiptJson is not null)
        {
            if (draft.ConfirmationRequestId != request.RequestId || draft.ConfirmationHash != confirmationHash)
                throw new ChatException(ChatError.ChatRequestConflict, "Este pedido já foi recebido. Confira o comprovante antes de enviar outra confirmação.");
            await transaction.CommitAsync(cancellationToken);
            return State(draft);
        }
        RequireActive(conversation);
        RequireDraftVersion(draft, request.ExpectedDraftVersion!.Value);
        await RequireCompletedTurnAsync(conversation, cancellationToken);
        var review = Read<PublicCheckoutReviewResponse>(draft.ReviewJson);
        if (review is null)
            throw new ChatException(ChatError.ChatOrderReviewRequired, "Revise o pedido antes de confirmar.");
        if (review.ReviewToken != request.ReviewToken)
            throw new OrderException(OrderError.OrderReviewChanged, "O resumo mudou. Revise novamente e confirme os valores atuais.");
        var input = Checkout(draft);
        ValidateInput(input);
        draft.OrderRequestId ??= Guid.NewGuid();
        var result = await checkout.CreateAsync(new(draft.OrderRequestId, review.ReviewToken, input), cancellationToken);
        // This ID comes exclusively from the request owned by this conversation.
        draft.OrderId = await database.Orders.Where(order => order.Origin == "DirectLink" && order.RequestId == draft.OrderRequestId)
            .Select(order => order.Id).SingleAsync(cancellationToken);
        draft.ConfirmationRequestId = request.RequestId;
        draft.ConfirmationHash = confirmationHash;
        draft.ReceiptJson = Serialize(result.Receipt);
        draft.UpdatedAt = Now();
        // Cancel any generation started before the explicit confirmation.
        conversation.AiLeaseId = null;
        conversation.AiLeaseExpiresAt = null;
        conversation.AiLeaseSourceSequence = null;
        conversation.AiProcessedSequence = await database.ChatMessages.Where(message => message.ConversationId == id && message.Kind == "Visitor")
            .MaxAsync(message => message.Sequence, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return State(draft);
    }

    private async Task<ChatOrderStateResponse> MutateAsync(Guid id, Guid lease, int source,
        Func<ChatConversation, ChatOrderDraft, Task<ChatOrderStateResponse>> action, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var conversation = await RequireLeaseAsync(id, lease, source, cancellationToken);
            var draft = await database.Set<ChatOrderDraft>().SingleOrDefaultAsync(row => row.ConversationId == id, cancellationToken)
                ?? new ChatOrderDraft { ConversationId = id, Name = conversation.VisitorName, UpdatedAt = Now() };
            RequireEditable(draft);
            var result = await action(conversation, draft);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // A rejected tool cannot leave tracked draft changes for the next tool in the same scope.
            foreach (var entry in database.ChangeTracker.Entries<ChatOrderDraft>().ToArray())
                entry.State = EntityState.Detached;
            throw;
        }
    }

    private async Task<ChatOrderStateResponse> ReviewDraftAsync(ChatOrderDraft draft, CancellationToken cancellationToken)
    {
        var state = State(draft);
        if (state.MissingFields.Length != 0)
            throw new ChatException(ChatError.ChatOrderIncomplete, "Ainda faltam dados para revisar: " + string.Join(", ", state.MissingFields.Select(FieldLabel)) + ".");
        var input = Checkout(draft);
        ValidateInput(input);
        var review = await checkout.ReviewAsync(input, cancellationToken);
        draft.ReviewJson = Serialize(review);
        draft.QuoteJson = Serialize(new PublicCartQuoteResponse(review.Items, review.Notes, review.Subtotal, review.CalculatedAt));
        return await SaveAsync(draft, cancellationToken);
    }

    private async Task<ChatOrderStateResponse> SaveAsync(ChatOrderDraft draft, CancellationToken cancellationToken)
    {
        if (database.Entry(draft).State == EntityState.Detached)
            database.Set<ChatOrderDraft>().Add(draft);
        draft.UpdatedAt = Now();
        await database.SaveChangesAsync(cancellationToken);
        return State(draft);
    }

    private async Task<ChatOrderStateResponse?> ReadStateAsync(Guid id, CancellationToken cancellationToken)
    {
        var draft = await database.Set<ChatOrderDraft>().AsNoTracking().SingleOrDefaultAsync(row => row.ConversationId == id, cancellationToken);
        return draft is null ? null : State(draft);
    }

    private async Task<ChatConversation> RequireLeaseAsync(Guid id, Guid lease, int source, CancellationToken cancellationToken)
    {
        var conversation = await LockAsync(id, cancellationToken);
        var lastVisitor = await database.ChatMessages.Where(message => message.ConversationId == id && message.Kind == "Visitor")
            .MaxAsync(message => (int?)message.Sequence, cancellationToken);
        if (conversation.AutomationMode != "Automatic" || conversation.Status != "Waiting"
            || conversation.AiLeaseId != lease || conversation.AiLeaseSourceSequence != source
            || conversation.AiLeaseExpiresAt is null || conversation.AiLeaseExpiresAt <= clock.GetUtcNow()
            || lastVisitor != source || conversation.CreatedAt.AddDays(7) <= clock.GetUtcNow())
            throw Invalid("O atendimento mudou. Atualize a conversa antes de continuar.");
        return conversation;
    }

    private async Task<ChatConversation> RequireVisitorAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await LockAsync(id, cancellationToken);
        RequireActive(conversation);
        return conversation;
    }

    private async Task RequireCompletedTurnAsync(ChatConversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.AutomationMode != "Automatic")
            return;
        var latestVisitor = await database.ChatMessages.Where(message => message.ConversationId == conversation.Id && message.Kind == "Visitor")
            .MaxAsync(message => message.Sequence, cancellationToken);
        if (latestVisitor > conversation.AiProcessedSequence
            || conversation.AiLeaseId is not null && conversation.AiLeaseExpiresAt > clock.GetUtcNow())
            throw new ChatException(ChatError.ChatAutomationPending, "Aguarde a atendente terminar a atualização do pedido antes de revisar ou confirmar.");
    }

    private async Task<ChatConversation> LockAsync(Guid id, CancellationToken cancellationToken) =>
        await database.ChatConversations.FromSqlInterpolated($"SELECT * FROM \"ChatConversations\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();

    private void RequireActive(ChatConversation conversation)
    {
        if (conversation.CreatedAt.AddDays(7) <= clock.GetUtcNow())
            throw Unavailable();
        if (conversation.Status == "Closed")
            throw new ChatException(ChatError.ChatClosed, "O atendimento foi encerrado.");
    }

    private static void RequireDraftVersion(ChatOrderDraft draft, int expected)
    {
        if (draft.Version != expected)
            throw new ChatException(ChatError.ChatDraftChanged, "O pedido em montagem mudou. Confira e revise antes de continuar.");
    }

    private static void RequireEditable(ChatOrderDraft draft)
    {
        if (draft.ReceiptJson is not null)
            throw new ChatException(ChatError.ChatOrderAlreadyCreated, "Este pedido já foi recebido. Alterações precisam ser tratadas com um atendente.");
    }

    private void Changed(ChatOrderDraft draft)
    {
        draft.Version++;
        draft.ReviewJson = null;
        draft.UpdatedAt = Now();
    }

    private static PublicCheckoutRequest Checkout(ChatOrderDraft draft) =>
        new(draft.Name, draft.Phone!, new(Read<CartItemRequest[]>(draft.ItemsJson)!, draft.Notes))
        { Fulfillment = draft.Fulfillment!, Address = Read<PublicDeliveryAddressRequest>(draft.AddressJson) };

    private static ChatOrderStateResponse State(ChatOrderDraft draft)
    {
        var quote = Read<PublicCartQuoteResponse>(draft.QuoteJson);
        var review = Read<PublicCheckoutReviewResponse>(draft.ReviewJson);
        var receipt = Read<PublicOrderReceipt>(draft.ReceiptJson);
        var items = Read<CartItemRequest[]>(draft.ItemsJson)!;
        var address = Read<PublicDeliveryAddressRequest>(draft.AddressJson);
        List<string> missing = [];
        if (items.Length == 0)
            missing.Add("items");
        if (string.IsNullOrWhiteSpace(draft.Name))
            missing.Add("name");
        if (string.IsNullOrWhiteSpace(draft.Phone))
            missing.Add("phone");
        if (draft.Fulfillment is null)
            missing.Add("fulfillment");
        if (draft.Fulfillment == "Delivery" && address is null)
            missing.Add("address");
        return new(draft.Version, quote?.Items ?? [], draft.Name, draft.Phone, draft.Fulfillment, address, draft.Notes,
            quote, review, receipt, missing.ToArray(), receipt is null && review is null && items.Length != 0);
    }

    // MVC validates HTTP bodies; tool invocations must perform the same nested validation explicitly.
    public static void ValidateInput(object value)
    {
        List<ValidationResult> failures = [];
        ValidateNode(value, failures);
        if (failures.Count != 0)
            throw Invalid(failures[0].ErrorMessage ?? "Confira os dados do pedido.");
    }

    private static void ValidateNode(object value, List<ValidationResult> failures)
    {
        Validator.TryValidateObject(value, new ValidationContext(value), failures, validateAllProperties: true);
        var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0).ToArray();
        // Positional record contracts put annotations on constructor parameters. MVC understands
        // those annotations, whereas Validator.TryValidateObject only inspects properties.
        foreach (var constructor in value.GetType().GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                var annotations = parameter.GetCustomAttributes<ValidationAttribute>().ToArray();
                var property = properties.SingleOrDefault(candidate => string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (annotations.Length == 0 || property is null)
                    continue;
                Validator.TryValidateValue(property.GetValue(value), new ValidationContext(value) { MemberName = property.Name }, failures, annotations);
            }
        }
        foreach (var property in properties)
        {
            var child = property.GetValue(value);
            if (child is null || child is string || child.GetType().IsValueType)
                continue;
            if (child is IEnumerable children)
            {
                foreach (var item in children)
                    if (item is not null && item is not string && !item.GetType().IsValueType)
                        ValidateNode(item, failures);
            }
            else
                ValidateNode(child, failures);
        }
    }

    private static string FieldLabel(string field) => field switch
    {
        "items" => "itens",
        "name" => "nome",
        "phone" => "telefone com DDD",
        "fulfillment" => "entrega ou retirada",
        "address" => "endereço de entrega",
        _ => field
    };
    private static T? Read<T>(string? json) => json is null ? default : JsonSerializer.Deserialize<T>(json);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim().Normalize();
    private DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static ChatException Invalid(string message) => new(ChatError.ChatVersionConflict, message);
    private static ChatException Unavailable() => new(ChatError.ChatUnavailable, "Conversa indisponível ou acesso expirado.");
}
