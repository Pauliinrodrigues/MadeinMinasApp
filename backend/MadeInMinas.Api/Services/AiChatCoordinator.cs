using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Services;

public sealed record AiChatLease(Guid ConversationId, Guid LeaseId, int SourceSequence);

/// <summary>Durable turn reservations; no transaction stays open during a provider call.</summary>
public sealed class AiChatCoordinator(AppDbContext database, IOptions<AiOptions> options, TimeProvider clock,
    IAiService provider, AiChatTools tools, ChatOrderService orders, ILogger<AiChatCoordinator> logger)
{
    public async Task<AiChatLease?> AcquireAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return null;
        database.ChangeTracker.Clear();
        var now = UtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var budgetLock = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"ai-chat-budget:{today:yyyy-MM-dd}")));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({budgetLock})", cancellationToken);
        var conversation = await database.ChatConversations.FromSqlInterpolated($"""
            SELECT c.* FROM "ChatConversations" c
            WHERE c."AutomationMode" = 'Automatic' AND c."Status" = 'Waiting'
              AND (c."AiLeaseId" IS NULL OR c."AiLeaseExpiresAt" <= {now})
              AND EXISTS (SELECT 1 FROM "ChatMessages" m WHERE m."ConversationId" = c."Id"
                AND m."Kind" = 'Visitor' AND m."Sequence" > c."AiProcessedSequence")
            ORDER BY c."UpdatedAt", c."Id" LIMIT 1 FOR UPDATE OF c SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var source = await database.ChatMessages.Where(message => message.ConversationId == conversation.Id && message.Kind == "Visitor")
            .MaxAsync(message => message.Sequence, cancellationToken);
        var reservedCalls = checked(options.Value.MaxToolCalls + 1);
        var usedCalls = await database.ChatConversations.Where(chat => chat.AiCallsDate == today)
            .SumAsync(chat => (long)chat.AiDailyCalls, cancellationToken);
        if (!options.Value.IsConfigured || conversation.CreatedAt.AddDays(7) <= now || conversation.Version >= 500
            || conversation.AiTotalCalls >= options.Value.MaxTurnsPerConversation
            || usedCalls + reservedCalls > options.Value.MaxDailyRequests)
        {
            FailToManual(conversation, source);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if (conversation.AiCallsDate != today)
        {
            conversation.AiCallsDate = today;
            conversation.AiDailyCalls = 0;
        }
        conversation.AiDailyCalls += reservedCalls;
        conversation.AiTotalCalls++;
        conversation.AiLeaseId = Guid.NewGuid();
        conversation.AiLeaseExpiresAt = now.AddSeconds(options.Value.TurnTimeoutSeconds + 30);
        conversation.AiLeaseSourceSequence = source;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AiChatLease(conversation.Id, conversation.AiLeaseId.Value, source);
    }

    public async Task ProcessAsync(AiChatLease lease, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TurnTimeoutSeconds));
        try
        {
            var history = await HistoryAsync(lease, timeout.Token);
            var state = await orders.GetStateAsync(lease.ConversationId, timeout.Token);
            var publicState = state is null ? null : new
            {
                state.DraftVersion,
                state.Name,
                hasPhone = state.Phone is not null,
                state.Fulfillment,
                state.PaymentPreference,
                state.ChangeFor,
                state.MissingFields,
                state.NeedsReview,
                items = state.Items.Select(item => new
                {
                    item.ProductId,
                    item.Name,
                    item.Quantity,
                    item.Notes,
                    item.UnitPrice,
                    item.LineTotal
                }).ToArray(),
                review = state.Review is null ? null : new { state.Review.Subtotal, state.Review.DeliveryFee, state.Review.Total },
                receipt = state.Receipt is null ? null : new { state.Receipt.Number, state.Receipt.Fulfillment, state.Receipt.Total }
            };
            var stateJson = JsonSerializer.Serialize(publicState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var request = new AiTurnRequest(lease.ConversationId, lease.LeaseId, lease.SourceSequence, history, stateJson);
            var result = await provider.ProcessMessageAsync(request,
                (call, token) => tools.ExecuteAsync(lease.ConversationId, lease.LeaseId, lease.SourceSequence, call, token), timeout.Token);
            await FinishAsync(lease, result, false, cancellationToken);
            logger.LogInformation("AI chat turn {ConversationId}/{SourceSequence}: input {InputTokens}, output {OutputTokens}.",
                lease.ConversationId, lease.SourceSequence, result.InputTokens, result.OutputTokens);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown leaves the durable reservation recoverable after its lease expires.
        }
        catch (Exception exception)
        {
            // Never log provider bodies, prompts, customer messages or credentials.
            logger.LogWarning("AI chat turn {ConversationId}/{SourceSequence} failed ({FailureType}).",
                lease.ConversationId, lease.SourceSequence, exception.GetType().Name);
            await FinishAsync(lease, null, true, cancellationToken);
        }
    }

    public async Task<bool> FinishAsync(AiChatLease lease, AiTurnResult? result, bool failed, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var conversation = await database.ChatConversations.FromSqlInterpolated($"SELECT * FROM \"ChatConversations\" WHERE \"Id\" = {lease.ConversationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null || conversation.AutomationMode != "Automatic" || conversation.Status != "Waiting"
            || conversation.AiLeaseId != lease.LeaseId || conversation.AiLeaseSourceSequence != lease.SourceSequence
            || conversation.AiLeaseExpiresAt is null || conversation.AiLeaseExpiresAt <= UtcNow())
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        var latestVisitor = await database.ChatMessages.Where(message => message.ConversationId == conversation.Id && message.Kind == "Visitor")
            .MaxAsync(message => message.Sequence, cancellationToken);
        if (latestVisitor != lease.SourceSequence)
        {
            ClearLease(conversation);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        if (failed || result is null || !options.Value.IsConfigured || string.IsNullOrWhiteSpace(result.Text) || result.Text.Length > 2000
            || conversation.Version >= 500)
            FailToManual(conversation, lease.SourceSequence);
        else
        {
            conversation.AiProcessedSequence = lease.SourceSequence;
            ClearLease(conversation);
            AddMessage(conversation, "Assistant", result.Text.Trim().Normalize());
            if (result.HandOff)
            {
                conversation.AutomationMode = "Manual";
                AddMessage(conversation, "System", "O atendimento automático encaminhou a conversa para a equipe, que responderá quando disponível.");
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<AiHistoryMessage>> HistoryAsync(AiChatLease lease, CancellationToken cancellationToken)
    {
        var messages = await database.ChatMessages.AsNoTracking()
            .Where(message => message.ConversationId == lease.ConversationId)
            .OrderByDescending(message => message.Sequence).Take(options.Value.MaxHistoryMessages)
            .Select(message => new { message.Kind, message.Text }).ToArrayAsync(cancellationToken);
        var history = new List<AiHistoryMessage>();
        var remaining = options.Value.MaxHistoryCharacters;
        foreach (var message in messages)
        {
            var text = message.Kind switch
            {
                "Staff" => "[Atendente humano] " + message.Text,
                "System" => "[Evento do atendimento] " + message.Text,
                _ => message.Text
            };
            if (text.Length > remaining)
                break;
            history.Add(new AiHistoryMessage(message.Kind == "Assistant" ? "assistant" : "user", text));
            remaining -= text.Length;
        }
        history.Reverse();
        return history;
    }

    private void FailToManual(ChatConversation conversation, int source)
    {
        conversation.AutomationMode = "Manual";
        conversation.AiProcessedSequence = source;
        ClearLease(conversation);
        AddMessage(conversation, "System", "O atendimento automático está indisponível. Sua mensagem foi preservada para a equipe, que responderá quando disponível.");
    }

    private void AddMessage(ChatConversation conversation, string kind, string text)
    {
        conversation.Version++;
        conversation.UpdatedAt = UtcNow();
        database.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Sequence = conversation.Version,
            Kind = kind,
            Text = text,
            CreatedAt = conversation.UpdatedAt
        });
    }

    private static void ClearLease(ChatConversation conversation)
    {
        conversation.AiLeaseId = null;
        conversation.AiLeaseExpiresAt = null;
        conversation.AiLeaseSourceSequence = null;
    }

    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
}
