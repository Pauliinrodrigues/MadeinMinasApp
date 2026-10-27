using System.Net;
using System.Text.Json;
using MadeInMinas.Api.Services.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Tests;

public sealed class GeminiAiProviderTests
{
    private static AiOptions Configured() => new()
    {
        Provider = "Gemini",
        Enabled = true,
        Model = "gemini-test-model",
        ApiKey = "test-only-not-a-real-key"
    };

    private static string Response(string name = "responder_cliente", string args = "{\"code\":\"Greeting\"}", string? id = "provider-call") =>
        JsonSerializer.Serialize(new
        {
            candidates = new[] { new
            {
                finishReason = "STOP",
                content = new
                {
                    role = "model",
                    parts = new object[]
                    {
                        new { text = "Texto livre do modelo não deve aparecer ao cliente." },
                        new { functionCall = new { id, name, args = JsonSerializer.Deserialize<JsonElement>(args) }, thoughtSignature = "opaque-signature" }
                    }
                }
            } },
            usageMetadata = new { promptTokenCount = 20, candidatesTokenCount = 5, thoughtsTokenCount = 3 }
        });

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static AiProviderRequest Request(IReadOnlyList<object>? input = null) =>
        new("server-instructions", input ?? [new { role = "user", content = "Olá" }], AiToolCatalog.All);

    [Fact]
    public async Task RequestKeepsKeyInHeaderAndUsesOnlyDeclaredFunctions()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(Response())));
        var result = await new GeminiAiProvider(new HttpClient(handler), Options.Create(Configured())).RespondAsync(
            Request([new { role = "user", content = "Olá" }, new { role = "assistant", content = "Mensagem anterior" }]), CancellationToken.None);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-test-model:generateContent", handler.Url);
        Assert.Equal("test-only-not-a-real-key", handler.Key);
        Assert.DoesNotContain("test-only", handler.Url);
        Assert.DoesNotContain("test-only", handler.Bodies[0]);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("server-instructions", body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("model", body.RootElement.GetProperty("contents")[1].GetProperty("role").GetString());
        Assert.Equal("ANY", body.RootElement.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString());
        Assert.Equal(600, body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
        var functions = body.RootElement.GetProperty("tools")[0].GetProperty("functionDeclarations");
        Assert.Equal(AiToolCatalog.All.Count, functions.GetArrayLength());
        foreach (var function in functions.EnumerateArray())
            Assert.False(function.GetProperty("parametersJsonSchema").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("provider-call", Assert.Single(result.Calls).Id);
        Assert.Equal(20, result.InputTokens);
        Assert.Equal(8, result.OutputTokens);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ToolRoundTripPreservesSignaturesAndMatchesResponseToTheOriginalCall(bool withProviderId)
    {
        var handler = new RecordingHandler((index, _) => Task.FromResult(Ok(index == 0
            ? Response("consultar_cardapio", "{\"search\":null,\"page\":1}", withProviderId ? "menu-call" : null)
            : Response("responder_cliente", "{\"code\":\"ToolResult\"}", "final-call"))));
        var settings = Options.Create(Configured());
        var service = new AiService(new GeminiAiProvider(new HttpClient(handler), settings), settings);
        var executions = 0;
        var result = await service.ProcessMessageAsync(new(Guid.NewGuid(), Guid.NewGuid(), 1, [new("user", "Quero o cardápio")], "null"),
            (call, _) =>
            {
                executions++;
                return Task.FromResult(call.Name == "consultar_cardapio"
                    ? new AiToolResult("{\"success\":true,\"data\":{\"price\":25.90}}", "Produto real: R$ 25,90.")
                    : new AiToolResult("{\"success\":true}"));
            }, CancellationToken.None);
        Assert.Equal(2, executions);
        Assert.Equal(2, handler.Count);
        Assert.Equal("Produto real: R$ 25,90.", result.Text);
        Assert.DoesNotContain("Texto livre", result.Text);
        using var body = JsonDocument.Parse(handler.Bodies[1]);
        var contents = body.RootElement.GetProperty("contents");
        var model = contents.EnumerateArray().Single(item => item.GetProperty("role").GetString() == "model");
        Assert.Equal("opaque-signature", model.GetProperty("parts")[1].GetProperty("thoughtSignature").GetString());
        var response = contents.EnumerateArray().Last().GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal("consultar_cardapio", response.GetProperty("name").GetString());
        Assert.Equal(25.90m, response.GetProperty("response").GetProperty("data").GetProperty("price").GetDecimal());
        if (withProviderId)
            Assert.Equal("menu-call", response.GetProperty("id").GetString());
        else
            Assert.False(response.TryGetProperty("id", out _));
        Assert.Equal(40, result.InputTokens);
        Assert.Equal(16, result.OutputTokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task QuotaAndProviderErrorsAreSanitizedAndNeverRetried(HttpStatusCode status)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("private-provider-body") }));
        var error = await Assert.ThrowsAsync<AiProviderException>(() => new GeminiAiProvider(new HttpClient(handler), Options.Create(Configured())).RespondAsync(Request(), CancellationToken.None));
        Assert.Equal($"Http{(int)status}", error.Reason);
        Assert.DoesNotContain("private-provider-body", error.ToString());
        Assert.DoesNotContain("test-only", error.ToString());
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"Pedido criado!\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"role\":\"model\",\"parts\":[{\"functionCall\":{\"name\":\"responder_cliente\",\"args\":{}}},{\"functionCall\":{\"name\":\"responder_cliente\",\"args\":{}}}]}}]}")]
    public async Task BlockedIncompleteOrParallelResponsesCannotExecuteAnyTool(string response)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(response)));
        var settings = Options.Create(Configured());
        var executed = false;
        await Assert.ThrowsAsync<AiProviderException>(() => new AiService(new GeminiAiProvider(new HttpClient(handler), settings), settings)
            .ProcessMessageAsync(new(Guid.NewGuid(), Guid.NewGuid(), 1, [new("user", "Oi")], "null"), (_, _) =>
            {
                executed = true;
                return Task.FromResult(new AiToolResult("{}"));
            }, CancellationToken.None));
        Assert.False(executed);
    }

    [Theory]
    [InlineData("criar_pedido", "{}")]
    [InlineData("execute_sql", "{}")]
    [InlineData("responder_cliente", "[]")]
    public async Task UnknownFunctionsAndInvalidArgumentsAreRejected(string name, string arguments)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(Response(name, arguments))));
        await Assert.ThrowsAsync<AiProviderException>(() => new GeminiAiProvider(new HttpClient(handler), Options.Create(Configured())).RespondAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ParameterlessCallsMayOmitOptionalArgumentsRoleAndProviderId()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Ok("""
            {"candidates":[{"finishReason":"STOP","content":{"parts":[{"functionCall":{"name":"consultar_atendimento"}}]}}]}
            """)));
        var result = await new GeminiAiProvider(new HttpClient(handler), Options.Create(Configured())).RespondAsync(Request(), CancellationToken.None);
        var call = Assert.Single(result.Calls);
        Assert.Equal("consultar_atendimento", call.Name);
        Assert.Equal("{}", call.ArgumentsJson);
        Assert.False(string.IsNullOrWhiteSpace(call.Id));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing-key")]
    [InlineData("wrong-provider")]
    [InlineData("invalid-model")]
    public async Task InactiveOrInvalidConfigurationNeverMakesAnExternalCall(string scenario)
    {
        var options = Configured();
        if (scenario == "disabled")
            options.Enabled = false;
        if (scenario == "missing-key")
            options.ApiKey = "";
        if (scenario == "wrong-provider")
            options.Provider = "OpenAI";
        if (scenario == "invalid-model")
            options.Model = "../other?key=in-query";
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Must not send"));
        await Assert.ThrowsAsync<AiProviderException>(() => new GeminiAiProvider(new HttpClient(handler), Options.Create(options)).RespondAsync(Request(), CancellationToken.None));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsing()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(new string('x', 262145))));
        var error = await Assert.ThrowsAsync<AiProviderException>(() => new GeminiAiProvider(new HttpClient(handler), Options.Create(Configured())).RespondAsync(Request(), CancellationToken.None));
        Assert.Equal("ResponseTooLarge", error.Reason);
    }

    [Fact]
    public async Task TransportFailuresHaveNoInfrastructureOrCredentialDetails()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("private-infrastructure-address"));
        var error = await Assert.ThrowsAsync<AiProviderException>(() => new GeminiAiProvider(new HttpClient(handler), Options.Create(Configured())).RespondAsync(Request(), CancellationToken.None));
        Assert.Equal("Network", error.Reason);
        Assert.DoesNotContain("private-infrastructure-address", error.ToString());
    }

    [Theory]
    [InlineData("Gemini", typeof(GeminiAiProvider))]
    [InlineData("OpenAI", typeof(OpenAiProvider))]
    public void DependencyInjectionSelectsOnlyTheConfiguredProvider(string selection, Type expected)
    {
        var services = new ServiceCollection();
        var options = Configured();
        options.Provider = selection;
        services.AddSingleton<IOptions<AiOptions>>(Options.Create(options));
        services.AddAiProvider();
        using var provider = services.BuildServiceProvider();
        Assert.IsType(expected, provider.GetRequiredService<IAiProvider>());
        Assert.True(options.IsValid());
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void UnsupportedProvidersDoNotPassConfigurationValidation()
    {
        var options = Configured();
        options.Provider = "unknown-provider";
        Assert.False(options.IsValid());
        Assert.False(options.IsConfigured);
    }

    private sealed class RecordingHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Url { get; private set; }
        public string? Key { get; private set; }
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Key = request.Headers.GetValues("x-goog-api-key").Single();
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return await respond(Count++, cancellationToken);
        }
    }
}
