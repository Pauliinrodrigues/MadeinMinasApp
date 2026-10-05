using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Chat;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class HumanChatService(AppDbContext database, PublicChatAccess access, TimeProvider clock)
{
    public async Task<ChatStartResult> StartAsync(StartChatRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim().Normalize();
        var text = request.Text.Trim().Normalize();
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { name, text })));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"human-chat:{request.RequestId}")));
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
        var existing = await database.ChatConversations.AsNoTracking().SingleOrDefaultAsync(chat => chat.RequestId == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                throw Conflict();
            await transaction.CommitAsync(cancellationToken);
            return new ChatStartResult(new(access.Issue(existing.Id, existing.CreatedAt), existing.CreatedAt), false);
        }
        var now = UtcNow();
        var conversation = new ChatConversation { RequestId = request.RequestId!.Value, RequestHash = hash, VisitorName = name, CreatedAt = now, UpdatedAt = now };
        database.ChatConversations.Add(conversation);
        database.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Sequence = 1,
            Kind = "Visitor",
            RequestId = request.RequestId,
            Text = text,
            CreatedAt = now
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChatStartResult(new(access.Issue(conversation.Id, now), now), true);
    }

    public async Task<ChatTranscriptResponse> ReadPublicAsync(string? token, int after, CancellationToken cancellationToken)
    {
        var id = VisitorId(token);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var conversation = await database.ChatConversations.AsNoTracking().SingleOrDefaultAsync(chat => chat.Id == id, cancellationToken) ?? throw Unavailable();
        var result = await TranscriptAsync(conversation, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ChatSendResult> SendPublicAsync(string? token, SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var id = VisitorId(token);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var conversation = await LockAsync(id, cancellationToken);
        var result = await SendAsync(conversation, request, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ChatPageResponse> ListAsync(ChatListQuery request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = database.ChatConversations.AsNoTracking().Where(chat => chat.Status == request.Status);
        var count = await query.CountAsync(cancellationToken);
        const int size = 20;
        var page = Math.Min(request.Page, Math.Max(1, (count + size - 1) / size));
        var ordered = request.Status == "Waiting"
            ? query.OrderBy(chat => chat.CreatedAt).ThenBy(chat => chat.Id)
            : query.OrderByDescending(chat => chat.UpdatedAt).ThenBy(chat => chat.Id);
        var rows = await ordered.Skip((page - 1) * size).Take(size).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChatPageResponse(rows.Select(Summary).ToArray(), page, size, count);
    }

    public async Task<StaffChatResponse> ReadStaffAsync(Guid id, int after, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var conversation = await database.ChatConversations.AsNoTracking().SingleOrDefaultAsync(chat => chat.Id == id, cancellationToken) ?? throw Unavailable();
        var result = new StaffChatResponse(Summary(conversation), await TranscriptAsync(conversation, after, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ChatSendResult> SendStaffAsync(Guid actorId, Guid stamp, Guid id, SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var (actor, _) = await ActorAsync(actorId, stamp, cancellationToken);
        var conversation = await LockAsync(id, cancellationToken);
        var result = await SendAsync(conversation, request, actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ChatSummaryResponse> ActAsync(Guid actorId, Guid stamp, Guid id, ChatActionRequest request, bool close, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var (actor, administrator) = await ActorAsync(actorId, stamp, cancellationToken);
        var conversation = await LockAsync(id, cancellationToken);
        var last = await database.ChatMessages.AsNoTracking().SingleAsync(message => message.ConversationId == id && message.Sequence == conversation.Version, cancellationToken);
        var actionText = close ? "Atendimento encerrado." : "Um atendente assumiu a conversa.";
        if (request.ExpectedVersion == conversation.Version - 1 && last.Kind == "System" && last.ActorId == actorId && last.Text == actionText)
        {
            await transaction.CommitAsync(cancellationToken);
            return Summary(conversation);
        }
        if (conversation.Version != request.ExpectedVersion)
            throw new ChatException(ChatError.ChatVersionConflict, "A conversa mudou. Atualize antes de continuar.");
        if (conversation.Status == "Closed")
            throw new ChatException(ChatError.ChatClosed, "O atendimento foi encerrado.");
        if (close)
        {
            if (conversation.Status != "InService" || conversation.AssignedToId != actorId)
                throw AssignmentRequired();
            conversation.Status = "Closed";
            conversation.ClosedAt = UtcNow();
        }
        else
        {
            if (conversation.Status == "InService" && (!administrator || conversation.AssignedToId == actorId))
                throw AssignmentRequired();
            conversation.AssignedToId = actorId;
            conversation.AssignedToName = actor.Name;
            conversation.Status = "InService";
        }
        AddMessage(conversation, "System", actionText, null, actor);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Summary(conversation);
    }

    private async Task<ChatSendResult> SendAsync(ChatConversation conversation, SendChatMessageRequest request, User? actor, CancellationToken cancellationToken)
    {
        var kind = actor is null ? "Visitor" : "Staff";
        var text = request.Text.Trim().Normalize();
        var existing = await database.ChatMessages.AsNoTracking().SingleOrDefaultAsync(message => message.ConversationId == conversation.Id && message.RequestId == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Kind != kind || existing.ActorId != actor?.Id || existing.Text != text)
                throw Conflict();
            return new ChatSendResult(Message(existing), false);
        }
        if (conversation.Status == "Closed")
            throw new ChatException(ChatError.ChatClosed, "O atendimento foi encerrado.");
        if (conversation.CreatedAt.AddDays(7) <= clock.GetUtcNow())
            throw new ChatException(ChatError.ChatExpired, "O acesso do visitante expirou. Encerre esta conversa.");
        if (actor is not null && (conversation.Status != "InService" || conversation.AssignedToId != actor.Id))
            throw AssignmentRequired();
        if (conversation.Version >= 500)
            throw new ChatException(ChatError.ChatLimitReached, "Esta conversa atingiu o limite de mensagens. Solicite o encerramento ao atendimento.");
        var message = AddMessage(conversation, kind, text, request.RequestId, actor);
        await database.SaveChangesAsync(cancellationToken);
        return new ChatSendResult(Message(message), true);
    }

    private ChatMessage AddMessage(ChatConversation conversation, string kind, string text, Guid? requestId, User? actor)
    {
        conversation.Version++;
        conversation.UpdatedAt = UtcNow();
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            Sequence = conversation.Version,
            RequestId = requestId,
            Kind = kind,
            Text = text,
            ActorId = actor?.Id,
            ActorName = actor?.Name,
            CreatedAt = conversation.UpdatedAt
        };
        database.ChatMessages.Add(message);
        return message;
    }

    private async Task<ChatTranscriptResponse> TranscriptAsync(ChatConversation conversation, int after, CancellationToken cancellationToken)
    {
        var messages = await database.ChatMessages.AsNoTracking().Where(message => message.ConversationId == conversation.Id && message.Sequence > after)
            .OrderBy(message => message.Sequence).Take(51)
            .Select(message => new ChatMessageResponse(message.Sequence, message.Kind, message.Text, message.CreatedAt)).ToArrayAsync(cancellationToken);
        return new ChatTranscriptResponse(conversation.Status, conversation.Version, conversation.CreatedAt, conversation.UpdatedAt, messages.Take(50).ToArray(), messages.Length > 50);
    }

    private async Task<ChatConversation> LockAsync(Guid id, CancellationToken cancellationToken) =>
        await database.ChatConversations.FromSqlInterpolated($"SELECT * FROM \"ChatConversations\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();

    private async Task<(User Actor, bool Administrator)> ActorAsync(Guid id, Guid stamp, CancellationToken cancellationToken)
    {
        var actor = await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !actor.IsActive || actor.SecurityStamp != stamp)
            throw new OrderException(OrderError.InvalidSession, "Sessão inválida. Faça login novamente.");
        var role = await database.Roles.Where(role => role.Id == actor.RoleId).Select(role => role.Code).SingleAsync(cancellationToken);
        if (!AccessPolicies.RolesByPermission[AccessPolicies.ManageChat].Contains(role))
            throw new OrderException(OrderError.PermissionDenied, "Acesso restrito ao administrador e atendimento.");
        return (actor, role == "Administrator");
    }

    private Guid VisitorId(string? token) => access.Read(token) ?? throw Unavailable();
    private static ChatException Unavailable() => new(ChatError.ChatUnavailable, "Conversa indisponível ou acesso expirado.");
    private static ChatException Conflict() => new(ChatError.ChatRequestConflict, "Esta tentativa já foi usada com outro conteúdo. Confira a conversa antes de repetir.");
    private static ChatException AssignmentRequired() => new(ChatError.ChatAssignmentRequired, "Assuma a conversa antes de responder ou encerrar. Somente um administrador pode assumir a conversa de outro atendente.");
    private static ChatMessageResponse Message(ChatMessage message) => new(message.Sequence, message.Kind, message.Text, message.CreatedAt);
    private static ChatSummaryResponse Summary(ChatConversation conversation) => new(conversation.Id, conversation.VisitorName, conversation.Status,
        conversation.AssignedToId, conversation.AssignedToName, conversation.Version, conversation.CreatedAt, conversation.UpdatedAt);
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
}
