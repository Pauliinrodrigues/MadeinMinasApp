using System.Text.Json;
using MadeInMinas.Api.Services;
using Npgsql;

namespace MadeInMinas.Api.Infrastructure;

// Bootstrap local executado pelo operador que já possui as credenciais do banco em User Secrets.
// A administração remota permanece autenticada pelas rotas /api/printing.
public static class PrintStationCommand
{
    public static async Task RunAsync(IServiceProvider services, IWebHostEnvironment environment, IConfiguration configuration)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("O cadastro local de estação exige o ambiente Development.");
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (connection.Database != "made_in_minas" || connection.Host is not ("localhost" or "127.0.0.1")
            || connection.Port != 5432 || connection.Username != "made_in_minas_app")
            throw new InvalidOperationException("O cadastro local aceita somente made_in_minas em localhost:5432.");
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", ".local", "print-agent"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "registration.json");
        if (File.Exists(path))
            throw new InvalidOperationException("Arquivo de cadastro existente preservado.");
        await using var scope = services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<PrintQueueService>();
        var settings = await queue.GetSettingsAsync(CancellationToken.None);
        if (settings.Registered)
            throw new InvalidOperationException("A estação já está cadastrada. Use a tela da equipe para uma substituição explícita.");
        var registered = await queue.RegisterAsync(settings.Version, CancellationToken.None);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            apiBaseUrl = "http://localhost:5080/api",
            token = registered.Key,
            printerName = "Diebold Procomp IM453HU_A",
            paperName = "IM4X3T/TSP143 76/80x500 mm"
        }));
        Console.WriteLine("Cadastro inicial criado em .local/print-agent/registration.json. A automação permanece desativada.");
    }
}
