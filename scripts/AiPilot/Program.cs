using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MadeInMinas.AiPilot;

// An explicit local-only utility, never executed by the application startup.
try
{
    var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AiPilot__ConnectionString"));
    var profile = Environment.GetEnvironmentVariable("AiPilot__Profile") ?? "Synthetic";
    RehearsalCatalog.ValidateTarget(connection, profile);
    var snapshot = profile == "Rehearsal" ? RehearsalCatalog.Read(Environment.GetEnvironmentVariable("AiPilot__SnapshotPath")!) : null;
    var password = Environment.GetEnvironmentVariable("AiPilot__AdminPassword");
    if (string.IsNullOrWhiteSpace(password) || password.Length < 24)
        throw new InvalidOperationException();

    await using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connection.ConnectionString).Options);
    await database.Database.MigrateAsync();
    await using var transaction = await database.Database.BeginTransactionAsync();
    if (!await database.Products.AnyAsync() && !await database.Users.AnyAsync()
        && !await database.Orders.AnyAsync() && !await database.ChatConversations.AnyAsync())
    {
        var now = DateTimeOffset.UtcNow;
        var category = new Category { Name = "Piloto (dados fictícios)", NormalizedName = "PILOTO (DADOS FICTÍCIOS)" };
        foreach (var (name, price, available) in profile == "Synthetic" ? new[] { ("Lanche piloto", 25.90m, true), ("Batata piloto", 12.50m, true), ("Lanche indisponível piloto", 30m, false) } : [])
        {
            var product = new Product
            {
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                Category = category,
                Description = "Produto fictício para testar o atendimento. Não representa o cardápio da loja.",
                Price = price,
                IsActive = true,
                IsAvailable = available,
                CreatedAt = now,
                UpdatedAt = now
            };
            var ingredient = new Ingredient
            {
                Name = "Insumo fictício " + name,
                NormalizedName = ("Insumo fictício " + name).ToUpperInvariant(),
                Unit = "un",
                CurrentStock = 1000m,
                CreatedAt = now,
                UpdatedAt = now
            };
            database.Recipes.Add(new Recipe
            {
                Product = product,
                YieldQuantity = 1,
                CreatedAt = now,
                UpdatedAt = now,
                Items = [new RecipeItem { Ingredient = ingredient, Quantity = 1m }]
            });
        }
        var administrator = new User { Name = "Administrador do piloto", Username = "piloto.admin", NormalizedUsername = "PILOTO.ADMIN", RoleId = 1 };
        administrator.PasswordHash = new PasswordHasher<User>().HashPassword(administrator, password);
        database.Users.Add(administrator);
        await database.SaveChangesAsync();
    }
    if (snapshot is not null)
        await RehearsalCatalog.ApplyAsync(database, snapshot, await database.Users.SingleAsync(user => user.Username == "piloto.admin"));
    await transaction.CommitAsync();
    Console.WriteLine("Migration aplicada no banco isolado. Dados do piloto preparados/preservados.");
}
catch
{
    // Configuration, database exceptions and stack traces can contain credentials.
    Console.Error.WriteLine("Não foi possível preparar o piloto. Confira o PostgreSQL isolado e a configuração local; nenhuma informação sensível foi exibida.");
    Environment.ExitCode = 1;
}
