using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class PaymentStatusHistoryConfiguration : IEntityTypeConfiguration<PaymentStatusHistory>
{
    public void Configure(EntityTypeBuilder<PaymentStatusHistory> builder)
    {
        builder.ToTable("PaymentStatusHistory", table =>
        {
            table.HasCheckConstraint("CK_PaymentStatusHistory_Transition", "(\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'Pending') OR (\"Version\" > 1 AND \"FromStatus\" IS NOT NULL AND ((\"FromStatus\" = 'Pending' AND \"ToStatus\" IN ('Received','Cancelled')) OR (\"FromStatus\" = 'Received' AND \"ToStatus\" = 'Refunded')))");
            table.HasCheckConstraint("CK_PaymentStatusHistory_Reason", "\"ToStatus\" NOT IN ('Cancelled','Refunded') OR (\"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
        });
        builder.HasKey(history => history.Id);
        builder.HasIndex(history => new { history.PaymentId, history.Version }).IsUnique();
        builder.Property(history => history.FromStatus).HasMaxLength(20);
        builder.Property(history => history.ToStatus).HasMaxLength(20).IsRequired();
        builder.Property(history => history.ActorName).HasMaxLength(120).IsRequired();
        builder.Property(history => history.Reason).HasMaxLength(500);
        builder.HasOne<User>().WithMany().HasForeignKey(history => history.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
