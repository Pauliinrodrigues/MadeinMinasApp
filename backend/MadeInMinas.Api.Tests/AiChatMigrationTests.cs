using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class AiChatMigrationTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private const string Previous = "20261010142620_AddStoreAttendanceSettings";
    private const string Current = "20261010152627_AddAiChatAgent";

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database => await database.ChatConversations.ExecuteDeleteAsync());
    public Task DisposeAsync() => factory.WithDatabaseAsync(async database =>
    {
        database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync("DELETE FROM \"ChatConversations\"");
        await database.GetService<IMigrator>().MigrateAsync();
    });

    [Fact]
    public async Task ApplyingMigrationPreservesLegacyVisitorHistoryAndDefaultsToManual()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var migrator = database.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            var id = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var hash = new string('A', 64);
            const string name = "Visitante anterior";
            const string text = "Conversa anterior à implementação de IA.";
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ChatConversations" ("Id", "RequestId", "RequestHash", "VisitorName", "Status", "Version", "CreatedAt", "UpdatedAt")
                VALUES ({id}, {requestId}, {hash}, {name}, 'Waiting', 1, {now}, {now})
                """);
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ChatMessages" ("Id", "ConversationId", "Sequence", "RequestId", "Kind", "Text", "CreatedAt")
                VALUES ({messageId}, {id}, 1, {requestId}, 'Visitor', {text}, {now})
                """);
            await migrator.MigrateAsync(Current);
            var conversation = await database.ChatConversations.AsNoTracking().SingleAsync(chat => chat.Id == id);
            Assert.Equal("Manual", conversation.AutomationMode);
            Assert.Equal("Waiting", conversation.Status);
            Assert.Equal(name, conversation.VisitorName);
            Assert.Equal(1, conversation.Version);
            Assert.Equal(0, conversation.AiProcessedSequence);
            Assert.Equal(0, conversation.AiTotalCalls);
            Assert.Null(conversation.AiLeaseId);
            Assert.False(await database.ChatOrderDrafts.AnyAsync());
            var message = await database.ChatMessages.AsNoTracking().SingleAsync(item => item.ConversationId == id);
            Assert.Equal(messageId, message.Id);
            Assert.Equal("Visitor", message.Kind);
            Assert.Equal(text, message.Text);
        });
    }

    [Theory]
    [InlineData("assistant")]
    [InlineData("anonymous-system")]
    [InlineData("draft")]
    [InlineData("usage")]
    [InlineData("automatic")]
    public async Task RevertingWithAiDataIsRejectedAndRollbackPreservesIt(string evidence)
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var now = DateTimeOffset.UtcNow;
            var conversation = new ChatConversation
            {
                RequestId = Guid.NewGuid(),
                RequestHash = new string('B', 64),
                VisitorName = "Maria",
                CreatedAt = now,
                UpdatedAt = now
            };
            database.ChatConversations.Add(conversation);
            database.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                Sequence = 1,
                RequestId = conversation.RequestId,
                Kind = "Visitor",
                Text = "Mensagem preservada.",
                CreatedAt = now
            });
            if (evidence is "assistant" or "anonymous-system")
            {
                conversation.Version = 2;
                database.ChatMessages.Add(new ChatMessage
                {
                    ConversationId = conversation.Id,
                    Sequence = 2,
                    Kind = evidence == "assistant" ? "Assistant" : "System",
                    Text = "Resposta automática preservada.",
                    CreatedAt = now
                });
            }
            if (evidence == "draft")
                database.ChatOrderDrafts.Add(new ChatOrderDraft { ConversationId = conversation.Id, Name = "Maria", UpdatedAt = now });
            if (evidence == "usage")
            {
                conversation.AiCallsDate = DateOnly.FromDateTime(now.UtcDateTime);
                conversation.AiDailyCalls = 9;
                conversation.AiTotalCalls = 1;
            }
            if (evidence == "automatic")
                conversation.AutomationMode = "Automatic";
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
            var script = database.GetService<IMigrator>().GenerateScript(Current, Previous, MigrationsSqlGenerationOptions.NoTransactions);
            await using (var transaction = await database.Database.BeginTransactionAsync())
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(script));
                Assert.Equal("P0001", exception.SqlState);
                Assert.Contains("AI chat data exists", exception.MessageText);
                await transaction.RollbackAsync();
            }
            var preserved = await database.ChatConversations.AsNoTracking().SingleAsync(chat => chat.Id == conversation.Id);
            Assert.Equal(conversation.Version, preserved.Version);
            Assert.Equal(conversation.AutomationMode, preserved.AutomationMode);
            Assert.Equal(conversation.AiTotalCalls, preserved.AiTotalCalls);
            Assert.Equal(conversation.Version, await database.ChatMessages.CountAsync(message => message.ConversationId == conversation.Id));
            Assert.Equal(evidence == "draft", await database.ChatOrderDrafts.AnyAsync(draft => draft.ConversationId == conversation.Id));
            Assert.Contains(Current, await database.Database.GetAppliedMigrationsAsync());
        });
    }
}
