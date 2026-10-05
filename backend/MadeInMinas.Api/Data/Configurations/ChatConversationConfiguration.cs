using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations", table =>
        {
            table.HasCheckConstraint("CK_ChatConversations_State", "(\"Status\" = 'Waiting' AND \"AssignedToId\" IS NULL AND \"AssignedToName\" IS NULL AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'InService' AND \"AssignedToId\" IS NOT NULL AND \"AssignedToName\" IS NOT NULL AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'Closed' AND \"AssignedToId\" IS NOT NULL AND \"AssignedToName\" IS NOT NULL AND \"ClosedAt\" IS NOT NULL)");
            table.HasCheckConstraint("CK_ChatConversations_Version", "\"Version\" > 0");
        });
        builder.HasKey(conversation => conversation.Id);
        builder.HasIndex(conversation => conversation.RequestId).IsUnique();
        builder.HasIndex(conversation => new { conversation.Status, conversation.UpdatedAt, conversation.Id });
        builder.Property(conversation => conversation.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(conversation => conversation.VisitorName).HasMaxLength(120).IsRequired();
        builder.Property(conversation => conversation.Status).HasMaxLength(20).IsRequired();
        builder.Property(conversation => conversation.AssignedToName).HasMaxLength(120);
        builder.HasOne<User>().WithMany().HasForeignKey(conversation => conversation.AssignedToId).OnDelete(DeleteBehavior.Restrict);
    }
}
