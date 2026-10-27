using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Services.Ai;

// No SDK-specific types escape this adapter. Credentials never reach the browser.
public sealed class OpenAiProvider(HttpClient client, IOptions<AiOptions> settings) : IAiProvider
{
    public async Task<AiProviderResponse> RespondAsync(AiProviderRequest request, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.IsConfigured || options.Provider != "OpenAI")
            throw new AiProviderException("NotConfigured");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = options.Model,
            instructions = request.Instructions,
            input = request.Input,
            store = false,
            include = new[] { "reasoning.encrypted_content" },
            max_output_tokens = options.MaxOutputTokens,
            parallel_tool_calls = false,
            tool_choice = "required",
            tools = request.Tools.Select(tool => new
            {
                type = "function",
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters,
                strict = true
            })
        });
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"Http{(int)response.StatusCode}");
            // Bound the response before parsing it; neither response bodies nor prompts are logged.
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
            using var json = JsonDocument.Parse(buffer.ToArray());
            var root = json.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed" ||
                !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                throw new AiProviderException("IncompleteResponse");
            var calls = new List<AiToolCall>();
            var items = new List<object>();
            foreach (var item in output.EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                // Reasoning items must accompany their function calls in the next request.
                if (type is "reasoning" or "function_call")
                    items.Add(item.Clone());
                if (type != "function_call")
                    continue;
                var id = item.GetProperty("call_id").GetString();
                var name = item.GetProperty("name").GetString();
                var arguments = item.GetProperty("arguments").GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || string.IsNullOrWhiteSpace(name) ||
                    arguments is null || arguments.Length > 16384)
                    throw new AiProviderException("InvalidToolCall");
                calls.Add(new AiToolCall(id, name, arguments));
            }
            var inputTokens = 0;
            var outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var usedInput))
                    inputTokens = usedInput.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var usedOutput))
                    outputTokens = usedOutput.GetInt32();
            }
            return new AiProviderResponse(calls, items, inputTokens, outputTokens);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("Timeout");
        }
        catch (HttpRequestException)
        {
            throw new AiProviderException("Network");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new AiProviderException("InvalidResponse");
        }
    }
}
