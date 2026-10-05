using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory", table =>
        {
            table.HasCheckConstraint("CK_OrderStatusHistory_Transition", "(\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'New') OR (\"Version\" > 1 AND \"FromStatus\" IS NOT NULL AND ((\"FromStatus\" = 'New' AND \"ToStatus\" IN ('Confirmed','Cancelled')) OR (\"FromStatus\" = 'Confirmed' AND \"ToStatus\" IN ('InPreparation','Cancelled')) OR (\"FromStatus\" = 'InPreparation' AND \"ToStatus\" IN ('Ready','Cancelled')) OR (\"FromStatus\" = 'Ready' AND \"ToStatus\" IN ('AwaitingDelivery','Delivered','Cancelled')) OR (\"FromStatus\" = 'AwaitingDelivery' AND \"ToStatus\" IN ('OutForDelivery','Cancelled')) OR (\"FromStatus\" = 'OutForDelivery' AND \"ToStatus\" IN ('Delivered','Cancelled')) OR (\"FromStatus\" = 'Delivered' AND \"ToStatus\" = 'Finalized')))");
            table.HasCheckConstraint("CK_OrderStatusHistory_Reason", "\"ToStatus\" <> 'Cancelled' OR (\"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
            table.HasCheckConstraint("CK_OrderStatusHistory_Actor", "\"ActorId\" IS NOT NULL OR (\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'New')");
        });
        builder.HasKey(history => history.Id);
        builder.HasIndex(history => new { history.OrderId, history.Version }).IsUnique();
        builder.Property(history => history.FromStatus).HasMaxLength(20);
        builder.Property(history => history.ToStatus).HasMaxLength(20).IsRequired();
        builder.Property(history => history.ActorName).HasMaxLength(120).IsRequired();
        builder.Property(history => history.Reason).HasMaxLength(500);
        builder.HasOne<User>().WithMany().HasForeignKey(history => history.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
