using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Chat;
using MadeInMinas.Api.Services;
using MadeInMinas.Api.Services.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class AiChatAutomationTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly FakeAiService ai = new();
    private int clientNumber;

    public Task InitializeAsync() => ClearAsync();
    public Task DisposeAsync() => ClearAsync();
    private Task ClearAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.ChatConversations.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });

    private WebApplicationFactory<Program> Host(bool enabled = true, bool key = true, int maximumTurns = 100, int dailyRequests = 300,
        int historyMessages = 20, int historyCharacters = 16000) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<AiOptions>(options =>
            {
                options.Enabled = enabled;
                options.ApiKey = key ? "test-only-never-sent" : "";
                options.Model = "test-only-model";
                options.MaxTurnsPerConversation = maximumTurns;
                options.MaxDailyRequests = dailyRequests;
                options.MaxHistoryMessages = historyMessages;
                options.MaxHistoryCharacters = historyCharacters;
            });
            foreach (var registration in services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(AiChatWorker)).ToArray())
                services.Remove(registration);
            services.RemoveAll<IAiService>();
            services.AddSingleton<IAiService>(ai);
        }));

    private HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-IP", "127.0.8." + Interlocked.Increment(ref clientNumber));
        return client;
    }

    private async Task<(Guid Id, string Token, StartChatRequest Input)> StartAsync(HttpClient client, string text = "Boa noite!")
    {
        var input = new StartChatRequest(Guid.NewGuid(), "Maria", text);
        var response = await client.PostAsJsonAsync("/api/public-chat", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var access = (await response.Content.ReadFromJsonAsync<StartChatResponse>())!.Access!;
        Guid id = default;
        await factory.WithDatabaseAsync(async database => id = await database.ChatConversations.Where(chat => chat.RequestId == input.RequestId).Select(chat => chat.Id).SingleAsync());
        return (id, access.Token, input);
    }

    private static async Task<ChatTranscriptResponse> ReadAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/public-chat");
        request.Headers.Add("X-Chat-Access", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatTranscriptResponse>())!;
    }

    private static async Task<HttpResponseMessage> PublicWriteAsync(HttpClient client, string token, string action, object value, bool put = false)
    {
        using var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, "/api/public-chat/" + action);
        request.Headers.Add("X-Chat-Access", token);
        request.Content = JsonContent.Create(value);
        return await client.SendAsync(request);
    }

    private async Task<HttpClient> StaffAsync(WebApplicationFactory<Program> host, int role = 2)
    {
        var user = await factory.CreateUserAsync(role);
        var client = Client(host);
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task ProcessOneAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
        var lease = await coordinator.AcquireAsync(default);
        Assert.NotNull(lease);
        await coordinator.ProcessAsync(lease, default);
    }

    [Fact]
    public async Task GreetingIsQueuedAndAssistantResponseUsesExistingTranscript()
    {
        using var host = Host();
        using var client = Client(host);
        var started = await StartAsync(client);
        Assert.Equal(0, ai.Calls);
        var pending = await ReadAsync(client, started.Token);
        Assert.Equal("Automatic", pending.AutomationMode);
        Assert.Equal("Pending", pending.AutomationState);
        await ProcessOneAsync(host);
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Idle", transcript.AutomationState);
        Assert.Equal(["Visitor", "Assistant"], transcript.Messages.Select(message => message.Kind));
        Assert.Equal("Boa noite! Sou o assistente virtual do Made in Minas.", transcript.Messages[^1].Text);
        Assert.Equal(1, ai.Calls);
        Assert.Equal("Boa noite!", ai.LastRequest!.History.Single().Text);
    }

    [Fact]
    public async Task StartAndMessageRetriesDoNotReserveAnotherTurnOrDuplicateResponses()
    {
        using var host = Host();
        using var client = Client(host);
        var started = await StartAsync(client);
        await ProcessOneAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/public-chat", started.Input)).StatusCode);
        var message = new SendChatMessageRequest(Guid.NewGuid(), "Quero ver o cardápio.");
        Assert.Equal(HttpStatusCode.Created, (await PublicWriteAsync(client, started.Token, "messages", message)).StatusCode);
        await ProcessOneAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await PublicWriteAsync(client, started.Token, "messages", message)).StatusCode);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default));
        Assert.Equal(2, ai.Calls);
        Assert.Equal(4, (await ReadAsync(client, started.Token)).Messages.Length);
    }

    [Fact]
    public async Task ProviderFailurePreservesMessageAndFallsBackToHumanWithoutTechnicalDetails()
    {
        ai.Handler = (_, _) => throw new InvalidOperationException("secret-provider-body-that-must-not-leak");
        using var host = Host();
        using var client = Client(host);
        var started = await StartAsync(client);
        await ProcessOneAsync(host);
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Manual", transcript.AutomationMode);
        Assert.Equal("Waiting", transcript.Status);
        Assert.Equal(["Visitor", "System"], transcript.Messages.Select(message => message.Kind));
        Assert.DoesNotContain("secret-provider", transcript.Messages[^1].Text);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default));
    }

    [Fact]
    public async Task DisabledAutomationPreservesLegacyManualFlow()
    {
        using var host = Host(enabled: false);
        using var client = Client(host);
        var started = await StartAsync(client);
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Manual", transcript.AutomationMode);
        Assert.False(transcript.AutomationAvailable);
        Assert.Equal(1, transcript.Version);
        Assert.Single(transcript.Messages);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default));
        Assert.Equal(0, ai.Calls);
    }

    [Fact]
    public async Task EnabledWithoutKeyMakesMissingConfigurationVisibleAndUsesManualMode()
    {
        using var host = Host(key: false);
        using var client = Client(host);
        var started = await StartAsync(client);
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Manual", transcript.AutomationMode);
        Assert.False(transcript.AutomationAvailable);
        Assert.Equal(["Visitor", "System"], transcript.Messages.Select(message => message.Kind));
        Assert.Contains("indisponível", transcript.Messages[^1].Text);
        Assert.Equal(0, ai.Calls);
    }

    [Fact]
    public async Task HumanClaimDuringProviderCallInvalidatesTheInFlightResponse()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ai.Handler = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return new AiTurnResult("Resposta atrasada"); };
        using var host = Host();
        using var client = Client(host);
        using var staff = await StaffAsync(host);
        var started = await StartAsync(client);
        await using var scope = host.Services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
        var lease = (await coordinator.AcquireAsync(default))!;
        var processing = coordinator.ProcessAsync(lease, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await staff.PutAsJsonAsync($"/api/chat/{started.Id}/claim", new ChatActionRequest(1))).EnsureSuccessStatusCode();
        release.SetResult();
        await processing;
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Manual", transcript.AutomationMode);
        Assert.Equal("InService", transcript.Status);
        Assert.DoesNotContain(transcript.Messages, message => message.Kind == "Assistant");
    }

    [Fact]
    public async Task NewVisitorMessageDiscardsStaleAnswerAndKeepsNewestTurnPending()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ai.Handler = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return new AiTurnResult("Resposta antiga"); };
        using var host = Host();
        using var client = Client(host);
        var started = await StartAsync(client);
        await using var scope = host.Services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
        var lease = (await coordinator.AcquireAsync(default))!;
        var processing = coordinator.ProcessAsync(lease, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await PublicWriteAsync(client, started.Token, "messages", new SendChatMessageRequest(Guid.NewGuid(), "Mudei de ideia."))).EnsureSuccessStatusCode();
        release.SetResult();
        await processing;
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Pending", transcript.AutomationState);
        Assert.DoesNotContain(transcript.Messages, message => message.Kind == "Assistant");
        ai.Handler = (_, _) => Task.FromResult(new AiTurnResult("Vou considerar sua última mensagem."));
        await ProcessOneAsync(host);
        Assert.Equal(2, ai.LastRequest!.SourceSequence);
        Assert.Single((await ReadAsync(client, started.Token)).Messages, message => message.Kind == "Assistant");
    }

    [Fact]
    public async Task PublicHandoffIsVersionedIdempotentAndFencesPendingLease()
    {
        using var host = Host();
        using var client = Client(host);
        var started = await StartAsync(client);
        await using var scope = host.Services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
        var lease = (await coordinator.AcquireAsync(default))!;
        Assert.Equal(HttpStatusCode.OK, (await PublicWriteAsync(client, started.Token, "handoff", new ChatActionRequest(1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublicWriteAsync(client, started.Token, "handoff", new ChatActionRequest(1), true)).StatusCode);
        Assert.False(await coordinator.FinishAsync(lease, new AiTurnResult("Resposta que não pode aparecer"), false, default));
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Manual", transcript.AutomationMode);
        Assert.Equal(2, transcript.Version);
        Assert.Single(transcript.Messages, message => message.Kind == "System");
        Assert.Equal(HttpStatusCode.Conflict, (await PublicWriteAsync(client, started.Token, "handoff", new ChatActionRequest(100), true)).StatusCode);
    }

    [Fact]
    public async Task ResponsibleAttendantCanResumeAutomationWithNewLeaseAndPreservedHistory()
    {
        using var host = Host();
        using var client = Client(host);
        using var staff = await StaffAsync(host);
        using var other = await StaffAsync(host);
        var started = await StartAsync(client);
        (await staff.PutAsJsonAsync($"/api/chat/{started.Id}/claim", new ChatActionRequest(1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await other.PutAsJsonAsync($"/api/chat/{started.Id}/automation", new ChatActionRequest(2))).StatusCode);
        (await staff.PutAsJsonAsync($"/api/chat/{started.Id}/automation", new ChatActionRequest(2))).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/chat/{started.Id}/automation", new ChatActionRequest(2))).EnsureSuccessStatusCode();
        await ProcessOneAsync(host);
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Automatic", transcript.AutomationMode);
        Assert.Equal("Waiting", transcript.Status);
        Assert.Equal(4, transcript.Version);
        Assert.Equal(1, ai.LastRequest!.SourceSequence);
        Assert.Contains(ai.LastRequest.History, message => message.Text.Contains("retomado pela equipe"));
    }

    [Fact]
    public async Task ExpiredLeaseIsRecoveredAndItsPreviousOwnerCannotCommit()
    {
        using var host = Host();
        using var client = Client(host);
        var started = await StartAsync(client);
        await using var firstScope = host.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
        var oldLease = (await first.AcquireAsync(default))!;
        await factory.WithDatabaseAsync(async database =>
            await database.ChatConversations.Where(chat => chat.Id == started.Id).ExecuteUpdateAsync(setters => setters.SetProperty(chat => chat.AiLeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
        await using var nextScope = host.Services.CreateAsyncScope();
        var next = nextScope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
        var lease = (await next.AcquireAsync(default))!;
        Assert.NotEqual(oldLease.LeaseId, lease.LeaseId);
        Assert.False(await first.FinishAsync(oldLease, new AiTurnResult("Resultado antigo"), false, default));
        Assert.True(await next.FinishAsync(lease, new AiTurnResult("Resultado recuperado"), false, default));
        Assert.False(await next.FinishAsync(lease, new AiTurnResult("Resultado repetido"), false, default));
        Assert.Single((await ReadAsync(client, started.Token)).Messages, message => message.Kind == "Assistant");
    }

    [Fact]
    public async Task ConcurrentInstancesReserveOneLeaseAndPersistDailyBudgetAcrossConversations()
    {
        using var host = Host(dailyRequests: 9);
        using var client = Client(host);
        var first = await StartAsync(client);
        await using var one = host.Services.CreateAsyncScope();
        await using var two = host.Services.CreateAsyncScope();
        var attempts = await Task.WhenAll(one.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default),
            two.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default));
        var lease = Assert.Single(attempts, result => result is not null)!;
        var owner = lease == attempts[0] ? one : two;
        Assert.True(await owner.ServiceProvider.GetRequiredService<AiChatCoordinator>().FinishAsync(lease, new AiTurnResult("Olá!"), false, default));
        var second = await StartAsync(client);
        await using var third = host.Services.CreateAsyncScope();
        Assert.Null(await third.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default));
        Assert.Equal("Manual", (await ReadAsync(client, second.Token)).AutomationMode);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(9, await database.ChatConversations.SumAsync(chat => chat.AiDailyCalls));
            Assert.Equal(1, (await database.ChatConversations.SingleAsync(chat => chat.Id == first.Id)).AiTotalCalls);
        });
    }

    [Fact]
    public async Task PerConversationLimitAndModelRequestedHandoffFallBackToManual()
    {
        using var host = Host(maximumTurns: 1);
        using var client = Client(host);
        var started = await StartAsync(client);
        await ProcessOneAsync(host);
        (await PublicWriteAsync(client, started.Token, "messages", new SendChatMessageRequest(Guid.NewGuid(), "Mais uma dúvida."))).EnsureSuccessStatusCode();
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AiChatCoordinator>().AcquireAsync(default));
        Assert.Equal("Manual", (await ReadAsync(client, started.Token)).AutomationMode);

        ai.Handler = (_, _) => Task.FromResult(new AiTurnResult("Vou chamar a equipe para ajudar.", HandOff: true));
        var next = await StartAsync(client);
        await ProcessOneAsync(host);
        var transcript = await ReadAsync(client, next.Token);
        Assert.Equal("Manual", transcript.AutomationMode);
        Assert.Equal(["Visitor", "Assistant", "System"], transcript.Messages.Select(message => message.Kind));
    }

    [Fact]
    public async Task ContextHistoryIsBoundedAndDoesNotEscalateMessagesToInstructions()
    {
        using var host = Host(historyMessages: 3, historyCharacters: 2000);
        using var client = Client(host);
        var started = await StartAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            var conversation = await database.ChatConversations.SingleAsync(chat => chat.Id == started.Id);
            for (var sequence = 2; sequence <= 25; sequence++)
                database.ChatMessages.Add(new MadeInMinas.Api.Models.ChatMessage
                {
                    ConversationId = started.Id,
                    Sequence = sequence,
                    RequestId = Guid.NewGuid(),
                    Kind = "Visitor",
                    Text = sequence.ToString("D2") + new string('x', 998),
                    CreatedAt = conversation.CreatedAt
                });
            conversation.Version = 25;
            await database.SaveChangesAsync();
        });
        await ProcessOneAsync(host);
        var history = ai.LastRequest!.History;
        Assert.InRange(history.Count, 1, 3);
        Assert.InRange(history.Sum(message => message.Text.Length), 1, 2000);
        Assert.StartsWith("25", history[^1].Text);
        Assert.All(history, message => Assert.Equal("user", message.Role));
        Assert.DoesNotContain(history, message => message.Text.Contains("Boa noite"));
    }

    [Fact]
    public async Task HandoffCredentialAndResumePermissionCannotBeForged()
    {
        using var host = Host();
        using var client = Client(host);
        using var kitchen = await StaffAsync(host, 3);
        var started = await StartAsync(client);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicWriteAsync(client, "invalid", "handoff", new ChatActionRequest(1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/chat/{started.Id}/automation", new ChatActionRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.PutAsJsonAsync($"/api/chat/{started.Id}/automation", new ChatActionRequest(1))).StatusCode);
        var transcript = await ReadAsync(client, started.Token);
        Assert.Equal("Automatic", transcript.AutomationMode);
        Assert.Equal(1, transcript.Version);
    }

    private sealed class FakeAiService : IAiService
    {
        public int Calls { get; private set; }
        public AiTurnRequest? LastRequest { get; private set; }
        public Func<AiTurnRequest, CancellationToken, Task<AiTurnResult>> Handler { get; set; } =
            (_, _) => Task.FromResult(new AiTurnResult("Boa noite! Sou o assistente virtual do Made in Minas."));
        public Task<AiTurnResult> ProcessMessageAsync(AiTurnRequest request,
            Func<AiToolCall, CancellationToken, Task<AiToolResult>> executeTool, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }
}
