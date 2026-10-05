using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages", table =>
        {
            table.HasCheckConstraint("CK_ChatMessages_Sequence", "\"Sequence\" > 0");
            table.HasCheckConstraint("CK_ChatMessages_Text", "length(btrim(\"Text\")) > 0");
            table.HasCheckConstraint("CK_ChatMessages_Author", "(\"Kind\" = 'Visitor' AND \"ActorId\" IS NULL AND \"ActorName\" IS NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'Staff' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'System' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NULL)");
        });
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => new { message.ConversationId, message.Sequence }).IsUnique();
        builder.HasIndex(message => new { message.ConversationId, message.RequestId }).IsUnique();
        builder.Property(message => message.Kind).HasMaxLength(20).IsRequired();
        builder.Property(message => message.Text).HasMaxLength(2000).IsRequired();
        builder.Property(message => message.ActorName).HasMaxLength(120);
        builder.HasOne<ChatConversation>().WithMany().HasForeignKey(message => message.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(message => message.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
