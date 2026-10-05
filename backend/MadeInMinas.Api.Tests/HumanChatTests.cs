using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Chat;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class HumanChatTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private const string PublicPath = "/api/public-chat";
    private const string StaffPath = "/api/chat";
    private static StartChatRequest Input() => new(Guid.NewGuid(), "  Maria  ", "  Tenho uma dúvida sobre o cardápio.  ");
    private static async Task ClearAsync(AppDbContext database)
    {
        await database.ChatConversations.ExecuteDeleteAsync();
        await database.StockMovements.ExecuteDeleteAsync();
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
    public Task InitializeAsync() => factory.WithDatabaseAsync(ClearAsync);
    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private async Task<(HttpClient Client, User User)> StaffAsync(int role = 2)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, user);
    }
    private async Task<(Guid Id, string Token, StartChatRequest Input)> StartAsync(HttpClient client)
    {
        var input = Input();
        var response = await client.PostAsJsonAsync(PublicPath, input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var created = (await response.Content.ReadFromJsonAsync<StartChatResponse>())!;
        var id = Guid.Empty;
        await factory.WithDatabaseAsync(async database => id = (await database.ChatConversations.SingleAsync(chat => chat.RequestId == input.RequestId)).Id);
        return (id, created.Access!.Token, input);
    }
    private static async Task<HttpResponseMessage> PublicAsync(HttpClient client, string? token, ChatMessageInputForTest? send = null, string suffix = "")
    {
        using var request = new HttpRequestMessage(send is null ? HttpMethod.Get : HttpMethod.Post, PublicPath + (send is null ? suffix : "/messages"));
        if (token is not null)
            request.Headers.Add("X-Chat-Access", token);
        if (send is not null)
            request.Content = JsonContent.Create(new SendChatMessageRequest(send.RequestId, send.Text));
        return await client.SendAsync(request);
    }
    private sealed record ChatMessageInputForTest(Guid RequestId, string Text);

    [Fact]
    public async Task PublicConversationIsIsolatedFromOtherConversationsAndBusinessData()
    {
        using var guest = factory.CreateStaffClient();
        var first = await StartAsync(guest);
        var second = await StartAsync(guest);
        guest.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        var response = await PublicAsync(guest, first.Token, suffix: "?id=" + second.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.Equal(["createdAt", "hasMore", "messages", "status", "updatedAt", "version"], body.RootElement.EnumerateObject().Select(field => field.Name).Order());
        Assert.Equal(["createdAt", "kind", "sequence", "text"], body.RootElement.GetProperty("messages")[0].EnumerateObject().Select(field => field.Name).Order());
        Assert.DoesNotContain(first.Id.ToString(), json);
        Assert.DoesNotContain("Maria", json);
        var message = new ChatMessageInputForTest(Guid.NewGuid(), "Somente na primeira");
        (await PublicAsync(guest, first.Token, message)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(message.Text, await (await PublicAsync(guest, second.Token)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(StaffPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/orders")).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(2, await database.ChatConversations.CountAsync());
            Assert.False(await database.Customers.AnyAsync());
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Payments.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad")]
    [InlineData("1234")]
    public async Task MissingOrInvalidAccessCannotReadOrSend(string? token)
    {
        using var guest = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(guest, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(guest, token, new(Guid.NewGuid(), "Mensagem"))).StatusCode);
    }

    [Fact]
    public async Task ExpiredTamperedAndOrderTrackingCredentialsAreRejected()
    {
        using var guest = factory.CreateStaffClient();
        var started = await StartAsync(guest);
        var provider = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var expired = provider.CreateProtector("MadeInMinas.HumanChat.v1").Protect(JsonSerializer.Serialize(new { Id = started.Id, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        var orderToken = factory.Services.GetRequiredService<PublicOrderAccess>().Issue(started.Id, DateTimeOffset.UtcNow)!.Token;
        foreach (var token in new[] { expired, orderToken, started.Token.Insert(30, "a"), new string('x', 2049) })
            Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(guest, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(guest, null, suffix: "?token=" + started.Token)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentStartAndMessageRetriesDoNotDuplicateData()
    {
        var input = Input();
        var clients = Enumerable.Range(0, 6).Select(_ => factory.CreateStaffClient()).ToArray();
        try
        {
            var responses = await Task.WhenAll(clients.Select(client => client.PostAsJsonAsync(PublicPath, input)));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            var results = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<StartChatResponse>()));
            Assert.All(results, result => Assert.Equal(results[0]!.Access!.ExpiresAt, result!.Access!.ExpiresAt));
            var token = results[0]!.Access!.Token;
            var message = new ChatMessageInputForTest(Guid.NewGuid(), "Uma única mensagem");
            var sends = await Task.WhenAll(clients.Select(client => PublicAsync(client, token, message)));
            Assert.Single(sends, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(5, sends.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(HttpStatusCode.Conflict, (await PublicAsync(clients[0], token, message with { Text = "Alterada" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await clients[0].PostAsJsonAsync(PublicPath, input with { Name = "Outro" })).StatusCode);
            await factory.WithDatabaseAsync(async database => { Assert.Equal(1, await database.ChatConversations.CountAsync()); Assert.Equal(2, await database.ChatMessages.CountAsync()); });
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    [Theory]
    [InlineData(1, HttpStatusCode.OK)]
    [InlineData(2, HttpStatusCode.OK)]
    [InlineData(3, HttpStatusCode.Forbidden)]
    [InlineData(4, HttpStatusCode.Forbidden)]
    public async Task OnlyAdministratorsAndAttendantsCanAccessStaffChat(int role, HttpStatusCode status)
    {
        using var guest = factory.CreateStaffClient();
        var started = await StartAsync(guest);
        var (staff, _) = await StaffAsync(role);
        using (staff)
        {
            Assert.Equal(status, (await staff.GetAsync(StaffPath)).StatusCode);
            Assert.Equal(status, (await staff.GetAsync(StaffPath + "/" + started.Id)).StatusCode);
            var claim = await staff.PutAsJsonAsync(StaffPath + "/" + started.Id + "/claim", new ChatActionRequest(1));
            Assert.Equal(status, claim.StatusCode);
        }
    }

    [Fact]
    public async Task ClaimReplyCloseAndLateRetryPreserveAuditWithoutExposingStaffIdentity()
    {
        using var guest = factory.CreateStaffClient();
        var started = await StartAsync(guest);
        var (staff, user) = await StaffAsync();
        using (staff)
        {
            var path = StaffPath + "/" + started.Id;
            var reply = new SendChatMessageRequest(Guid.NewGuid(), "Olá, como posso ajudar?");
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(path + "/messages", reply)).StatusCode);
            (await staff.PutAsJsonAsync(path + "/claim", new ChatActionRequest(1))).EnsureSuccessStatusCode();
            (await staff.PutAsJsonAsync(path + "/claim", new ChatActionRequest(1))).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Created, (await staff.PostAsJsonAsync(path + "/messages", reply)).StatusCode);
            var publicMessage = new ChatMessageInputForTest(Guid.NewGuid(), "Obrigado");
            (await PublicAsync(guest, started.Token, publicMessage)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(path + "/close", new ChatActionRequest(3))).StatusCode);
            (await staff.PutAsJsonAsync(path + "/close", new ChatActionRequest(4))).EnsureSuccessStatusCode();
            (await staff.PutAsJsonAsync(path + "/close", new ChatActionRequest(4))).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync(path + "/messages", reply)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PublicAsync(guest, started.Token, publicMessage)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PublicAsync(guest, started.Token, new(Guid.NewGuid(), "Depois do fim"))).StatusCode);
            var json = await (await PublicAsync(guest, started.Token)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(user.Name, json);
            Assert.DoesNotContain(user.Id.ToString(), json);
            var transcript = JsonSerializer.Deserialize<ChatTranscriptResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("Closed", transcript.Status);
            Assert.Equal(5, transcript.Version);
            Assert.Equal(["Visitor", "System", "Staff", "Visitor", "System"], transcript.Messages.Select(message => message.Kind));
            await factory.WithDatabaseAsync(async database =>
            {
                var actorMessages = await database.ChatMessages.Where(message => message.Kind != "Visitor").ToArrayAsync();
                Assert.All(actorMessages, message => { Assert.Equal(user.Id, message.ActorId); Assert.Equal(user.Name, message.ActorName); });
                Assert.NotNull((await database.ChatConversations.SingleAsync()).ClosedAt);
            });
        }
    }

    [Fact]
    public async Task ConcurrentClaimsHaveOneWinnerAndOnlyAdministratorCanTakeOver()
    {
        using var guest = factory.CreateStaffClient();
        var started = await StartAsync(guest);
        var (first, _) = await StaffAsync();
        var (second, _) = await StaffAsync();
        var (admin, _) = await StaffAsync(1);
        using (first)
        using (second)
        using (admin)
        {
            var path = StaffPath + "/" + started.Id;
            var responses = await Task.WhenAll(first.PutAsJsonAsync(path + "/claim", new ChatActionRequest(1)), second.PutAsJsonAsync(path + "/claim", new ChatActionRequest(1)));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var loser = responses[0].StatusCode == HttpStatusCode.OK ? second : first;
            Assert.Equal(HttpStatusCode.Conflict, (await loser.PutAsJsonAsync(path + "/claim", new ChatActionRequest(2))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await loser.PostAsJsonAsync(path + "/messages", new SendChatMessageRequest(Guid.NewGuid(), "Não autorizado"))).StatusCode);
            (await admin.PutAsJsonAsync(path + "/claim", new ChatActionRequest(2))).EnsureSuccessStatusCode();
            (await admin.PutAsJsonAsync(path + "/close", new ChatActionRequest(3))).EnsureSuccessStatusCode();
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("text")]
    [InlineData("longText")]
    [InlineData("requestId")]
    [InlineData("actorId")]
    [InlineData("status")]
    public async Task InvalidOrInjectedStartDataIsRejected(string field)
    {
        using var guest = factory.CreateStaffClient();
        var input = JsonSerializer.SerializeToNode(Input(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (field is "name" or "text")
            input[field] = "   ";
        else if (field == "longText")
            input["text"] = new string('a', 2001);
        else if (field == "requestId")
            input[field] = Guid.Empty;
        else
            input[field] = "forged";
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync(PublicPath, input)).StatusCode);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.ChatConversations.AnyAsync()));
    }

    [Fact]
    public async Task MessagesArePagedAndLimitStillAllowsClosure()
    {
        using var guest = factory.CreateStaffClient();
        var started = await StartAsync(guest);
        await factory.WithDatabaseAsync(async database =>
        {
            var chat = await database.ChatConversations.SingleAsync();
            for (var sequence = 2; sequence <= 500; sequence++)
                database.ChatMessages.Add(new ChatMessage { ConversationId = chat.Id, Sequence = sequence, RequestId = Guid.NewGuid(), Kind = "Visitor", Text = "Mensagem " + sequence, CreatedAt = chat.CreatedAt });
            chat.Version = 500;
            await database.SaveChangesAsync();
        });
        var first = (await (await PublicAsync(guest, started.Token)).Content.ReadFromJsonAsync<ChatTranscriptResponse>())!;
        Assert.Equal(50, first.Messages.Length);
        Assert.True(first.HasMore);
        Assert.Equal(1, first.Messages[0].Sequence);
        var last = (await (await PublicAsync(guest, started.Token, suffix: "?after=450")).Content.ReadFromJsonAsync<ChatTranscriptResponse>())!;
        Assert.Equal(50, last.Messages.Length);
        Assert.False(last.HasMore);
        Assert.Equal(500, last.Messages[^1].Sequence);
        Assert.Equal(HttpStatusCode.Conflict, (await PublicAsync(guest, started.Token, new(Guid.NewGuid(), "Limite"))).StatusCode);
        var (staff, _) = await StaffAsync();
        using (staff)
        {
            (await staff.PutAsJsonAsync(StaffPath + "/" + started.Id + "/claim", new ChatActionRequest(500))).EnsureSuccessStatusCode();
            (await staff.PutAsJsonAsync(StaffPath + "/" + started.Id + "/close", new ChatActionRequest(501))).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task RateLimitsAreIndependentAndMigrationCannotEraseConversations()
    {
        using var guest = factory.CreateStaffClient();
        var started = await StartAsync(guest);
        for (var index = 1; index < 5; index++)
            (await guest.PostAsJsonAsync(PublicPath, started.Input)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guest.PostAsJsonAsync(PublicPath, started.Input)).StatusCode);
        (await PublicAsync(guest, started.Token)).EnsureSuccessStatusCode();
        for (var index = 0; index < 20; index++)
            (await PublicAsync(guest, started.Token, new(Guid.NewGuid(), "Mensagem"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PublicAsync(guest, started.Token, new(Guid.NewGuid(), "Mensagem"))).StatusCode);
        (await PublicAsync(guest, started.Token)).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
        {
            var sql = database.GetService<IMigrator>().GenerateScript("20261005183152_AddHumanChat", "20261005145517_AddPublicOrders", MigrationsSqlGenerationOptions.NoTransactions);
            await using var transaction = await database.Database.BeginTransactionAsync();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains("Chat conversations exist", failure.MessageText);
            await transaction.RollbackAsync();
            Assert.Equal(1, await database.ChatConversations.CountAsync());
        });
    }
}
