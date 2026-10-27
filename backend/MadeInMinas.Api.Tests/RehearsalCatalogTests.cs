using System.Text.Json;
using MadeInMinas.AiPilot;
using MadeInMinas.Api.DTOs.StoreAttendance;
using Npgsql;

namespace MadeInMinas.Api.Tests;

public sealed class RehearsalCatalogTests
{
    public static RehearsalSnapshot Sample() => new(1, DateTimeOffset.UtcNow,
        [new(new Guid("10000000-0000-0000-0000-000000000001"), "Categoria do ensaio")],
        [new(new Guid("20000000-0000-0000-0000-000000000001"), new Guid("10000000-0000-0000-0000-000000000001"), "Produto do ensaio", "Descrição comercial autorizada.", 20m, true)],
        new(Enumerable.Range(1, 7).Select(day => new StoreOpeningHours(day, "18:30", "00:00")).ToArray(), ["Card", "Cash", "Pix"], "Prazo variável.", false, false),
        [new("corinto", "Todos os bairros", "Corinto", "MG", 5m, true)], new(5m, 0, "Endereço público da loja"));

    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("database")]
    [InlineData("user")]
    [InlineData("ssl")]
    [InlineData("profile")]
    [InlineData("cross-profile")]
    public void TargetGuardRejectsOperationalAndMismatchedConnections(string change)
    {
        var connection = new NpgsqlConnectionStringBuilder("Host=127.0.0.1;Port=55435;Database=made_in_minas_ai_rehearsal;Username=ai_pilot;SSL Mode=Disable");
        var profile = "Rehearsal";
        switch (change)
        {
            case "host":
                connection.Host = "localhost";
                break;
            case "port":
                connection.Port = 5432;
                break;
            case "database":
                connection.Database = "made_in_minas";
                break;
            case "user":
                connection.Username = "postgres";
                break;
            case "ssl":
                connection.SslMode = SslMode.Require;
                break;
            case "profile":
                profile = "Production";
                break;
            default:
                profile = "Synthetic";
                break;
        }
        Assert.Throws<InvalidOperationException>(() => RehearsalCatalog.ValidateTarget(connection, profile));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("price")]
    [InlineData("category")]
    [InlineData("duplicates")]
    [InlineData("hours")]
    [InlineData("payment")]
    [InlineData("fee")]
    [InlineData("outside-hours")]
    public void InvalidSnapshotCannotReachImport(string change)
    {
        var sample = Sample();
        var invalid = change switch
        {
            "schema" => sample with { SchemaVersion = 2 },
            "price" => sample with { Products = [sample.Products[0] with { Price = -1m }] },
            "category" => sample with { Products = [sample.Products[0] with { CategoryId = Guid.NewGuid() }] },
            "duplicates" => sample with { Products = [sample.Products[0], sample.Products[0]] },
            "hours" => sample with { Attendance = sample.Attendance with { Hours = [] } },
            "payment" => sample with { Attendance = sample.Attendance with { PaymentMethods = ["Unauthorized"] } },
            "fee" => sample with { Areas = [sample.Areas[0] with { Fee = 0 }] },
            _ => sample with { Attendance = sample.Attendance with { ReceiveOrdersOutsideHours = true } }
        };
        Assert.ThrowsAny<Exception>(() => RehearsalCatalog.Validate(invalid));
    }

    [Fact]
    public void ValidPublicSnapshotAndDedicatedTargetsAreAccepted()
    {
        RehearsalCatalog.Validate(Sample());
        foreach (var (profile, name) in new[] { ("Synthetic", "made_in_minas_ai_pilot"), ("Rehearsal", "made_in_minas_ai_rehearsal") })
            RehearsalCatalog.ValidateTarget(new NpgsqlConnectionStringBuilder($"Host=127.0.0.1;Port=55435;Database={name};Username=ai_pilot;SSL Mode=Disable"), profile);
    }

    [Fact]
    public void AdministrativeFieldsInSnapshotAreRejected()
    {
        var json = JsonSerializer.Serialize(Sample(), RehearsalCatalog.Json);
        var injected = json[..^1] + ",\"unitCost\":18.1234}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RehearsalSnapshot>(injected, RehearsalCatalog.Json));
    }
}
