using System.ComponentModel.DataAnnotations;
using System.Text;
using MadeInMinas.Api.Services;

namespace MadeInMinas.Api.Infrastructure;

public static class AdministratorCommand
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
        {
            Console.WriteLine("Execute --create-admin diretamente no terminal do Rider para digitar a senha de forma oculta.");
            Environment.ExitCode = 1;
            return;
        }

        Console.Write("Nome do administrador: ");
        var name = Console.ReadLine() ?? "";
        Console.Write("Login: ");
        var username = Console.ReadLine() ?? "";
        var password = ReadPassword("Senha (15 a 128 caracteres): ");
        if (password != ReadPassword("Confirme a senha: "))
        {
            Console.WriteLine("As senhas nao conferem. Nenhum usuario foi criado.");
            Environment.ExitCode = 1;
            return;
        }

        await using var scope = services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorBootstrap>()
                .CreateAsync(name, username, password);
            Console.WriteLine("Administrador criado. Nenhuma senha foi exibida ou gravada em arquivo.");
        }
        catch (Exception exception) when (exception is ValidationException or InvalidOperationException)
        {
            Console.WriteLine(exception.Message);
            Environment.ExitCode = 1;
        }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return password.ToString();
    }
}
