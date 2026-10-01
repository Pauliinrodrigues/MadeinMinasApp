using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" BETWEEN 1 AND 99");
            table.HasCheckConstraint("CK_OrderItems_Position", "\"Position\" BETWEEN 1 AND 50");
            table.HasCheckConstraint("CK_OrderItems_Amounts", "\"UnitPrice\" > 0 AND \"LineTotal\" = \"UnitPrice\" * \"Quantity\"");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ProductName).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Notes).HasMaxLength(250);
        builder.Property(item => item.UnitPrice).HasPrecision(8, 2);
        builder.Property(item => item.LineTotal).HasPrecision(10, 2);
        builder.HasIndex(item => new { item.OrderId, item.Position }).IsUnique();
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
