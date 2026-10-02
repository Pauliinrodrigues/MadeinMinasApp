using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class OrderStockComponentConfiguration : IEntityTypeConfiguration<OrderStockComponent>
{
    public void Configure(EntityTypeBuilder<OrderStockComponent> builder)
    {
        builder.ToTable("OrderStockComponents", table =>
        {
            table.HasCheckConstraint("CK_OrderStockComponents_Quantities", "\"ProductQuantity\" BETWEEN 1 AND 99 AND \"RecipeYield\" BETWEEN 1 AND 10000 AND \"RecipeQuantity\" BETWEEN 0.001 AND 999999.999 AND \"ConsumedQuantity\" BETWEEN 0.001 AND 999999.999");
            table.HasCheckConstraint("CK_OrderStockComponents_Unit", "\"Unit\" IN ('kg', 'l', 'un')");
        });
        builder.HasKey(item => new { item.OrderId, item.ProductId, item.IngredientId });
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Ingredient>().WithMany().HasForeignKey(item => item.IngredientId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.ProductName).HasMaxLength(120).IsRequired();
        builder.Property(item => item.IngredientName).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Unit).HasMaxLength(2).IsRequired();
        builder.Property(item => item.RecipeQuantity).HasPrecision(9, 3);
        builder.Property(item => item.ConsumedQuantity).HasPrecision(9, 3);
    }
}
