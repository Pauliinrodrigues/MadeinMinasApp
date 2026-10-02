using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.ToTable("Ingredients", table =>
        {
            table.HasCheckConstraint("CK_Ingredients_Unit", "\"Unit\" IN ('kg', 'l', 'un')");
            table.HasCheckConstraint("CK_Ingredients_UnitCost", "\"UnitCost\" BETWEEN 0 AND 999999.9999");
            table.HasCheckConstraint("CK_Ingredients_MinimumStock", "\"MinimumStock\" BETWEEN 0 AND 999999.999");
            table.HasCheckConstraint("CK_Ingredients_CurrentStock", "\"CurrentStock\" BETWEEN 0 AND 999999.999");
            table.HasCheckConstraint("CK_Ingredients_StockVersion", "\"StockVersion\" >= 0");
        });
        builder.HasKey(ingredient => ingredient.Id);
        builder.Property(ingredient => ingredient.Name).HasMaxLength(120).IsRequired();
        builder.Property(ingredient => ingredient.NormalizedName).HasMaxLength(120).IsRequired();
        builder.Property(ingredient => ingredient.Unit).HasMaxLength(2).IsRequired();
        builder.Property(ingredient => ingredient.UnitCost).HasPrecision(10, 4);
        builder.Property(ingredient => ingredient.MinimumStock).HasPrecision(9, 3);
        builder.Property(ingredient => ingredient.CurrentStock).HasPrecision(9, 3);
        builder.Property(ingredient => ingredient.Supplier).HasMaxLength(150);
        builder.HasIndex(ingredient => ingredient.NormalizedName).IsUnique();
    }
}
