using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Services.Ai;

// Converts the application-owned history/tools to Gemini REST, without changing the chat/domain.
public sealed class GeminiAiProvider(HttpClient client, IOptions<AiOptions> settings) : IAiProvider
{
    public async Task<AiProviderResponse> RespondAsync(AiProviderRequest request, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.IsConfigured || options.Provider != "Gemini")
            throw new AiProviderException("NotConfigured");
        // Only a model ID is allowed, never a URL, path or query parameter.
        if (!Regex.IsMatch(options.Model, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}\z", RegexOptions.CultureInvariant))
            throw new AiProviderException("InvalidModel");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{options.Model}:generateContent");
            message.Headers.Add("x-goog-api-key", options.ApiKey);
            message.Content = JsonContent.Create(new
            {
                systemInstruction = new { parts = new[] { new { text = request.Instructions } } },
                contents = Contents(request.Input),
                tools = new[] { new { functionDeclarations = request.Tools.Select(tool => new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parametersJsonSchema = tool.Parameters
                }) } },
                toolConfig = new { functionCallingConfig = new { mode = "ANY" } },
                generationConfig = new { candidateCount = 1, maxOutputTokens = options.MaxOutputTokens }
            });
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"Http{(int)response.StatusCode}");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + count > 262144)
                    throw new AiProviderException("ResponseTooLarge");
                await buffer.WriteAsync(chunk.AsMemory(0, count), timeout.Token);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            var candidates = root.GetProperty("candidates");
            if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() != 1)
                throw new AiProviderException("InvalidToolSequence");
            var candidate = candidates[0];
            if (candidate.GetProperty("finishReason").GetString() != "STOP")
                throw new AiProviderException("IncompleteResponse");
            var content = candidate.GetProperty("content");
            if (content.TryGetProperty("role", out var role) && role.GetString() != "model")
                throw new AiProviderException("InvalidResponse");
            var calls = content.GetProperty("parts").EnumerateArray()
                .Where(part => part.TryGetProperty("functionCall", out _)).ToArray();
            // Gemini can propose parallel calls. Never execute them concurrently or partially.
            if (calls.Length != 1)
                throw new AiProviderException("InvalidToolSequence");
            var function = calls[0].GetProperty("functionCall");
            var name = function.GetProperty("name").GetString();
            var arguments = function.TryGetProperty("args", out var args) ? args : JsonSerializer.SerializeToElement(new Dictionary<string, object>());
            if (string.IsNullOrWhiteSpace(name) || !request.Tools.Any(tool => tool.Name == name)
                || arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 16384)
                throw new AiProviderException("InvalidToolCall");
            var providerId = function.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (providerId is not null && (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 200))
                throw new AiProviderException("InvalidToolCall");
            var call = new AiToolCall(providerId ?? Guid.NewGuid().ToString("N"), name, arguments.GetRawText());
            // Preserve the complete original content, including opaque thought signatures.
            // Internal IDs used for idempotency are not inserted into unsigned provider parts.
            var modelContent = content.TryGetProperty("role", out _) ? content.Clone()
                : JsonSerializer.SerializeToElement(new { role = "model", parts = content.GetProperty("parts") });
            var turn = new GeminiToolTurn(call.Id, name, providerId, modelContent);
            var inputTokens = 0;
            var outputTokens = 0;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                inputTokens = Tokens(usage, "promptTokenCount");
                outputTokens = checked(Tokens(usage, "candidatesTokenCount") + Tokens(usage, "thoughtsTokenCount"));
            }
            return new AiProviderResponse([call], [turn], inputTokens, outputTokens);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("Timeout");
        }
        catch (HttpRequestException)
        {
            throw new AiProviderException("Network");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new AiProviderException("InvalidResponse");
        }
    }

    private static List<object> Contents(IReadOnlyList<object> input)
    {
        var contents = new List<object>();
        var calls = new Dictionary<string, GeminiToolTurn>(StringComparer.Ordinal);
        foreach (var item in input)
        {
            if (item is GeminiToolTurn turn)
            {
                calls[turn.CallId] = turn;
                contents.Add(turn.Content);
                continue;
            }
            var value = JsonSerializer.SerializeToElement(item);
            if (value.TryGetProperty("type", out var type) && type.GetString() == "function_call_output")
            {
                var callId = value.GetProperty("call_id").GetString();
                if (callId is null || !calls.TryGetValue(callId, out var previous))
                    throw new AiProviderException("InvalidToolSequence");
                using var result = JsonDocument.Parse(value.GetProperty("output").GetString()!);
                if (result.RootElement.ValueKind != JsonValueKind.Object)
                    throw new AiProviderException("InvalidToolOutput");
                var functionResponse = new Dictionary<string, object>
                {
                    ["name"] = previous.Name,
                    ["response"] = result.RootElement.Clone()
                };
                if (previous.ProviderId is not null)
                    functionResponse["id"] = previous.ProviderId;
                contents.Add(new { role = "user", parts = new[] { new { functionResponse } } });
            }
            else
            {
                var role = value.GetProperty("role").GetString();
                if (role is not ("user" or "assistant"))
                    throw new AiProviderException("InvalidHistory");
                contents.Add(new { role = role == "assistant" ? "model" : "user", parts = new[] { new { text = value.GetProperty("content").GetString() } } });
            }
        }
        return contents;
    }

    private static int Tokens(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var value))
            return 0;
        if (!value.TryGetInt32(out var count) || count < 0)
            throw new AiProviderException("InvalidUsage");
        return count;
    }

    private sealed record GeminiToolTurn(string CallId, string Name, string? ProviderId, JsonElement Content);
}
