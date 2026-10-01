using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Method", "\"Method\" IN ('Cash','Pix','CreditCard','DebitCard')");
            table.HasCheckConstraint("CK_Payments_Status", "\"Status\" IN ('Pending','Received','Cancelled','Refunded') AND \"Version\" >= 1");
            table.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_Payments_Cash", "(\"Method\" = 'Cash' AND \"Status\" IN ('Received','Refunded') AND \"CashTendered\" IS NOT NULL AND \"CashTendered\" >= \"Amount\") OR ((\"Method\" <> 'Cash' OR \"Status\" IN ('Pending','Cancelled')) AND \"CashTendered\" IS NULL)");
        });
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(payment => payment.Method).HasMaxLength(20).IsRequired();
        builder.Property(payment => payment.Status).HasMaxLength(20).IsRequired();
        builder.Property(payment => payment.Amount).HasPrecision(12, 2);
        builder.Property(payment => payment.CashTendered).HasPrecision(12, 2);
        builder.HasIndex(payment => new { payment.CreatedById, payment.RequestId }).IsUnique();
        builder.HasIndex(payment => payment.OrderId).IsUnique().HasDatabaseName("IX_Payments_ActiveOrder")
            .HasFilter("\"Status\" IN ('Pending','Received')");
        builder.HasIndex(payment => new { payment.OrderId, payment.CreatedAt, payment.Id });
        builder.HasOne<Order>().WithMany().HasForeignKey(payment => payment.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(payment => payment.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(payment => payment.History).WithOne().HasForeignKey(history => history.PaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
