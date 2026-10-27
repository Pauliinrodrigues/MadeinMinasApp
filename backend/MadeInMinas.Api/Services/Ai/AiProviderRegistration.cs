using Microsoft.Extensions.Options;

namespace MadeInMinas.Api.Services.Ai;

public static class AiProviderRegistration
{
    public static IServiceCollection AddAiProvider(this IServiceCollection services)
    {
        services.AddHttpClient<OpenAiProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<GeminiAiProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddTransient<IAiProvider>(provider => provider.GetRequiredService<IOptions<AiOptions>>().Value.Provider switch
        {
            "Gemini" => provider.GetRequiredService<GeminiAiProvider>(),
            "OpenAI" => provider.GetRequiredService<OpenAiProvider>(),
            _ => throw new AiProviderException("UnsupportedProvider")
        });
        return services;
    }
}
