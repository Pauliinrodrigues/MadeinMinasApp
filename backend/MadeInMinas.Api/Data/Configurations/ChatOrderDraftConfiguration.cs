using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class ChatOrderDraftConfiguration : IEntityTypeConfiguration<ChatOrderDraft>
{
    public void Configure(EntityTypeBuilder<ChatOrderDraft> builder)
    {
        builder.ToTable("ChatOrderDrafts", table =>
        {
            table.HasCheckConstraint("CK_ChatOrderDrafts_RequestedPayment", "(\"PaymentPreference\" IS NULL OR \"PaymentPreference\" IN ('Card','Cash','Pix')) AND (\"ChangeFor\" IS NULL OR (\"PaymentPreference\" IS NOT NULL AND \"PaymentPreference\" = 'Cash' AND \"ChangeFor\" > 0 AND \"ChangeFor\" <= 999999.99))");
            table.HasCheckConstraint("CK_ChatOrderDrafts_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_ChatOrderDrafts_Fulfillment", "\"Fulfillment\" IS NULL OR \"Fulfillment\" IN ('Pickup','Delivery')");
            table.HasCheckConstraint("CK_ChatOrderDrafts_Receipt", "(\"ReceiptJson\" IS NULL AND \"OrderId\" IS NULL AND \"ConfirmationRequestId\" IS NULL AND \"ConfirmationHash\" IS NULL) OR (\"ReceiptJson\" IS NOT NULL AND \"OrderId\" IS NOT NULL AND \"OrderRequestId\" IS NOT NULL AND \"ConfirmationRequestId\" IS NOT NULL AND \"ConfirmationHash\" IS NOT NULL)");
        });
        builder.HasKey(draft => draft.ConversationId);
        builder.Property(draft => draft.Name).HasMaxLength(120).IsRequired();
        builder.Property(draft => draft.Phone).HasMaxLength(30);
        builder.Property(draft => draft.Fulfillment).HasMaxLength(10);
        builder.Property(draft => draft.Notes).HasMaxLength(500);
        builder.Property(draft => draft.PaymentPreference).HasMaxLength(10);
        builder.Property(draft => draft.ChangeFor).HasPrecision(12, 2);
        builder.Property(draft => draft.ConfirmationHash).HasMaxLength(64);
        builder.Property(draft => draft.ItemsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(draft => draft.AddressJson).HasColumnType("jsonb");
        builder.Property(draft => draft.QuoteJson).HasColumnType("jsonb");
        builder.Property(draft => draft.ReviewJson).HasColumnType("jsonb");
        builder.Property(draft => draft.ReceiptJson).HasColumnType("jsonb");
        builder.HasIndex(draft => draft.OrderRequestId).IsUnique();
        builder.HasIndex(draft => draft.OrderId).IsUnique();
        builder.HasOne<ChatConversation>().WithOne().HasForeignKey<ChatOrderDraft>(draft => draft.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Order>().WithOne().HasForeignKey<ChatOrderDraft>(draft => draft.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
