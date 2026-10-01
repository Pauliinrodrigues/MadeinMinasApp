using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Number", "\"Number\" > 0");
            table.HasCheckConstraint("CK_Orders_Status", "\"Status\" IN ('New','Confirmed','Cancelled') AND \"Version\" >= 1");
            table.HasCheckConstraint("CK_Orders_Origin", "\"Origin\" = 'Manual'");
            table.HasCheckConstraint("CK_Orders_Amounts", "\"Subtotal\" > 0 AND \"DeliveryFee\" BETWEEN 0 AND 9999.99 AND \"Total\" = \"Subtotal\" + \"DeliveryFee\"");
            table.HasCheckConstraint("CK_Orders_Fulfillment", "(\"Fulfillment\" = 'Pickup' AND \"AddressId\" IS NULL AND \"AddressStreet\" IS NULL AND \"AddressNumber\" IS NULL AND \"AddressNeighborhood\" IS NULL AND \"AddressCity\" IS NULL AND \"AddressState\" IS NULL AND \"AddressComplement\" IS NULL AND \"AddressPostalCode\" IS NULL AND \"AddressReference\" IS NULL AND \"DeliveryFee\" = 0) OR (\"Fulfillment\" = 'Delivery' AND \"AddressId\" IS NOT NULL AND \"AddressStreet\" IS NOT NULL AND \"AddressNumber\" IS NOT NULL AND \"AddressNeighborhood\" IS NOT NULL AND \"AddressCity\" IS NOT NULL AND \"AddressState\" IS NOT NULL)");
        });
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Number).UseIdentityByDefaultColumn();
        builder.HasIndex(order => order.Number).IsUnique();
        builder.HasIndex(order => new { order.CreatedById, order.RequestId }).IsUnique();
        builder.HasIndex(order => new { order.CreatedAt, order.Number });
        builder.HasIndex(order => new { order.Status, order.CreatedAt, order.Number });
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAt, order.Number });
        builder.Property(order => order.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(order => order.CustomerName).HasMaxLength(120).IsRequired();
        builder.Property(order => order.CustomerPhone).HasMaxLength(14).IsRequired();
        builder.Property(order => order.Fulfillment).HasMaxLength(10).IsRequired();
        builder.Property(order => order.Origin).HasMaxLength(20).IsRequired();
        builder.Property(order => order.Status).HasMaxLength(20).IsRequired();
        builder.Property(order => order.Notes).HasMaxLength(500);
        builder.Property(order => order.AddressStreet).HasMaxLength(120);
        builder.Property(order => order.AddressNumber).HasMaxLength(20);
        builder.Property(order => order.AddressNeighborhood).HasMaxLength(80);
        builder.Property(order => order.AddressCity).HasMaxLength(80);
        builder.Property(order => order.AddressState).HasMaxLength(2);
        builder.Property(order => order.AddressComplement).HasMaxLength(120);
        builder.Property(order => order.AddressPostalCode).HasMaxLength(8);
        builder.Property(order => order.AddressReference).HasMaxLength(250);
        builder.Property(order => order.Subtotal).HasPrecision(12, 2);
        builder.Property(order => order.DeliveryFee).HasPrecision(6, 2);
        builder.Property(order => order.Total).HasPrecision(12, 2);
        builder.HasOne<Customer>().WithMany().HasForeignKey(order => order.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Address>().WithMany().HasForeignKey(order => order.AddressId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(order => order.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.History).WithOne().HasForeignKey(history => history.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
