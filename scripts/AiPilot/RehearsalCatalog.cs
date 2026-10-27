using System.Text.Json;
using System.Text.Json.Serialization;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Delivery;
using MadeInMinas.Api.DTOs.StoreAttendance;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MadeInMinas.AiPilot;

public sealed record RehearsalCategory(Guid Id, string Name);
public sealed record RehearsalProduct(Guid Id, Guid CategoryId, string Name, string? Description, decimal Price, bool IsAvailable);
public sealed record RehearsalArea(string Id, string? Neighborhood, string City, string State, decimal Fee, bool CoversAllNeighborhoods);
public sealed record RehearsalOptions(decimal? DeliveryFee, decimal PickupFee, string? PickupAddress);
public sealed record RehearsalSnapshot(int SchemaVersion, DateTimeOffset CapturedAt, RehearsalCategory[] Categories,
    RehearsalProduct[] Products, StoreAttendanceData Attendance, RehearsalArea[] Areas, RehearsalOptions Options);

public static class RehearsalCatalog
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static void ValidateTarget(NpgsqlConnectionStringBuilder connection, string profile)
    {
        var expected = profile switch
        {
            "Synthetic" => "made_in_minas_ai_pilot",
            "Rehearsal" => "made_in_minas_ai_rehearsal",
            _ => throw new InvalidOperationException()
        };
        if (connection.Host != "127.0.0.1" || connection.Port != 55435 || connection.Database != expected
            || connection.Username != "ai_pilot" || connection.SslMode != SslMode.Disable)
            throw new InvalidOperationException();
    }

    public static RehearsalSnapshot Read(string path)
    {
        if (new FileInfo(path).Length > 2_000_000)
            throw new InvalidOperationException();
        var snapshot = JsonSerializer.Deserialize<RehearsalSnapshot>(File.ReadAllText(path), Json) ?? throw new InvalidOperationException();
        Validate(snapshot);
        return snapshot;
    }

    public static void Validate(RehearsalSnapshot snapshot)
    {
        static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
        static bool Money(decimal value) => value >= 0 && value <= 999999.99m && decimal.Round(value, 2) == value;
        if (snapshot.SchemaVersion != 1 || snapshot.Products is not { Length: > 0 and <= 480 }
            || snapshot.Categories is not { Length: > 0 and <= 480 } || snapshot.Attendance is null
            || snapshot.Options is null || snapshot.Areas is not { Length: <= 100 })
            throw new InvalidOperationException();
        if (snapshot.Categories.Any(item => item is null || item.Id == Guid.Empty || !Text(item.Name, 120))
            || snapshot.Categories.Select(item => item.Id).Distinct().Count() != snapshot.Categories.Length
            || snapshot.Categories.Select(item => item.Name.Trim().Normalize().ToUpperInvariant()).Distinct().Count() != snapshot.Categories.Length)
            throw new InvalidOperationException();
        if (snapshot.Products.Any(item => item is null || item.Id == Guid.Empty || !Text(item.Name, 160)
                || item.Description is { Length: > 1000 } || !Money(item.Price) || !snapshot.Categories.Any(category => category.Id == item.CategoryId))
            || snapshot.Products.Select(item => item.Id).Distinct().Count() != snapshot.Products.Length
            || snapshot.Products.Select(item => item.Name.Trim().Normalize().ToUpperInvariant()).Distinct().Count() != snapshot.Products.Length)
            throw new InvalidOperationException();
        if (snapshot.Options.PickupFee != 0 || snapshot.Options.DeliveryFee is { } fee && !Money(fee)
            || snapshot.Options.PickupAddress is { Length: > 500 }
            || snapshot.Areas.Any(area => area is null || !Text(area.Id, 100) || !Text(area.City, 120) || !Text(area.State, 2) || area.State.Length != 2
                || !Money(area.Fee) || !area.CoversAllNeighborhoods && !Text(area.Neighborhood, 120)
                || snapshot.Options.DeliveryFee is { } fixedFee && area.Fee != fixedFee)
            || snapshot.Areas.Select(area => area.Id).Distinct().Count() != snapshot.Areas.Length
            || snapshot.Attendance.ReceiveOrdersOutsideHours != false)
            throw new InvalidOperationException();
        ChatOrderService.ValidateInput(snapshot.Attendance);
        foreach (var area in snapshot.Areas)
            ChatOrderService.ValidateInput(new DeliveryAreaRequest(area.Id, area.Neighborhood ?? "Todos os bairros", area.City, area.State, area.Fee, true) { CoversAllNeighborhoods = area.CoversAllNeighborhoods });
    }

    // The caller validates the target BEFORE migrations/connection and owns its
    // transaction. IDs come from the public catalog. No operational DB is read.
    public static async Task ApplyAsync(AppDbContext database, RehearsalSnapshot snapshot, User administrator)
    {
        Validate(snapshot);
        var now = DateTimeOffset.UtcNow;
        var categories = await database.Categories.ToDictionaryAsync(item => item.Id);
        foreach (var source in snapshot.Categories)
        {
            if (!categories.TryGetValue(source.Id, out var category))
            {
                category = new Category { Id = source.Id };
                database.Categories.Add(category);
            }
            category.Name = source.Name;
            category.NormalizedName = source.Name.Trim().Normalize().ToUpperInvariant();
            category.IsActive = true;
        }
        foreach (var category in categories.Values.Where(item => !snapshot.Categories.Any(source => source.Id == item.Id)))
            category.IsActive = false;
        var products = await database.Products.ToDictionaryAsync(item => item.Id);
        foreach (var source in snapshot.Products)
        {
            if (!products.TryGetValue(source.Id, out var product))
            {
                product = new Product { Id = source.Id, CreatedAt = now };
                database.Products.Add(product);
                var ingredient = new Ingredient { Name = "Insumo fictício ensaio " + source.Id, NormalizedName = ("INSUMO FICTÍCIO ENSAIO " + source.Id).ToUpperInvariant(), Unit = "un", CurrentStock = 1000m };
                database.Recipes.Add(new Recipe { Product = product, YieldQuantity = 1, Items = [new RecipeItem { Ingredient = ingredient, Quantity = 1m }] });
            }
            product.CategoryId = source.CategoryId;
            product.Name = source.Name;
            product.NormalizedName = source.Name.Trim().Normalize().ToUpperInvariant();
            product.Description = source.Description;
            product.Price = source.Price;
            product.IsActive = true;
            product.IsAvailable = source.IsAvailable;
            product.UpdatedAt = now;
        }
        foreach (var product in products.Values.Where(item => !snapshot.Products.Any(source => source.Id == item.Id)))
            product.IsActive = false;
        var attendance = await database.StoreAttendanceSettings.SingleOrDefaultAsync();
        if (attendance is null)
        { attendance = new StoreAttendanceSettings(); database.StoreAttendanceSettings.Add(attendance); }
        // Public snapshots use web casing; persisted settings must use the SAME
        // format as StoreAttendanceService and DeliverySettingsService writers.
        attendance.SettingsJson = JsonSerializer.Serialize(snapshot.Attendance);
        attendance.Version++;
        attendance.UpdatedAt = now;
        attendance.UpdatedById = administrator.Id;
        attendance.UpdatedByName = administrator.Name;
        var delivery = await database.DeliverySettings.SingleOrDefaultAsync();
        if (delivery is null)
        { delivery = new DeliverySettings(); database.DeliverySettings.Add(delivery); }
        delivery.AreasJson = JsonSerializer.Serialize(snapshot.Areas.Select(area => new DeliveryAreaResponse(area.Id, area.Neighborhood ?? "Todos os bairros", area.City, area.State, area.Fee, true) { CoversAllNeighborhoods = area.CoversAllNeighborhoods }).ToArray());
        delivery.Version++;
        delivery.UpdatedAt = now;
        delivery.UpdatedById = administrator.Id;
        delivery.UpdatedByName = administrator.Name;
        await database.SaveChangesAsync();
    }
}
