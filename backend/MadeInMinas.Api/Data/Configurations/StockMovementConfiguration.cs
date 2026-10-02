using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_Values", "\"Quantity\" BETWEEN 0 AND 999999.999 AND \"PreviousBalance\" BETWEEN 0 AND 999999.999 AND \"Balance\" BETWEEN 0 AND 999999.999 AND \"Delta\" <> 0 AND \"Balance\" = \"PreviousBalance\" + \"Delta\" AND \"Version\" > 0");
            table.HasCheckConstraint("CK_StockMovements_Type", "(\"Type\" = 'Entry' AND \"Quantity\" > 0 AND \"Delta\" = \"Quantity\") OR (\"Type\" = 'Exit' AND \"Quantity\" > 0 AND \"Delta\" = -\"Quantity\") OR (\"Type\" = 'Count' AND \"Balance\" = \"Quantity\")");
            table.HasCheckConstraint("CK_StockMovements_Unit", "\"Unit\" IN ('kg', 'l', 'un')");
        });
        builder.HasKey(movement => movement.Id);
        builder.HasOne<Ingredient>().WithMany().HasForeignKey(movement => movement.IngredientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(movement => movement.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(movement => movement.ActorName).HasMaxLength(120).IsRequired();
        builder.Property(movement => movement.IngredientName).HasMaxLength(120).IsRequired();
        builder.Property(movement => movement.Unit).HasMaxLength(2).IsRequired();
        builder.Property(movement => movement.Type).HasMaxLength(5).IsRequired();
        builder.Property(movement => movement.Reason).HasMaxLength(500).IsRequired();
        builder.Property(movement => movement.Quantity).HasPrecision(9, 3);
        builder.Property(movement => movement.Delta).HasPrecision(9, 3);
        builder.Property(movement => movement.PreviousBalance).HasPrecision(9, 3);
        builder.Property(movement => movement.Balance).HasPrecision(9, 3);
        builder.HasIndex(movement => new { movement.IngredientId, movement.RequestId }).IsUnique();
        builder.HasIndex(movement => new { movement.IngredientId, movement.Version }).IsUnique();
    }
}
