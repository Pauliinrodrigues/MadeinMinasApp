namespace MadeInMinas.Api.Services.Ai;

public sealed class AiOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public int TurnTimeoutSeconds { get; set; } = 90;
    public int MaxToolCalls { get; set; } = 8;
    public int MaxHistoryMessages { get; set; } = 20;
    public int MaxHistoryCharacters { get; set; } = 16000;
    public int MaxContextCharacters { get; set; } = 48000;
    public int MaxOutputTokens { get; set; } = 600;
    public int MaxTurnsPerConversation { get; set; } = 100;
    public int MaxDailyRequests { get; set; } = 300;

    public bool IsConfigured => Enabled && IsSupportedProvider &&
        !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);

    private bool IsSupportedProvider => Provider is "OpenAI" or "Gemini";

    public bool IsValid() => IsSupportedProvider && TimeoutSeconds is >= 5 and <= 120 &&
        TurnTimeoutSeconds >= TimeoutSeconds && TurnTimeoutSeconds <= 300 && MaxToolCalls is >= 1 and <= 12 &&
        MaxHistoryMessages is >= 2 and <= 50 && MaxHistoryCharacters is >= 2000 and <= 40000 &&
        MaxContextCharacters >= MaxHistoryCharacters + 12000 && MaxContextCharacters <= 100000 &&
        MaxOutputTokens is >= 200 and <= 4000 && MaxTurnsPerConversation is >= 1 and <= 200 &&
        MaxDailyRequests >= MaxToolCalls + 1 && MaxDailyRequests <= 100000;
}
