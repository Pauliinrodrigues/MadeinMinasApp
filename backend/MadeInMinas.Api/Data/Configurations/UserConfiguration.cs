using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table =>
            table.HasCheckConstraint("CK_Users_FailedLoginAttempts", "\"FailedLoginAttempts\" >= 0"));
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Name).HasMaxLength(120).IsRequired();
        builder.Property(user => user.Username).HasMaxLength(64).IsRequired();
        builder.Property(user => user.NormalizedUsername).HasMaxLength(64).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(user => user.NormalizedUsername).IsUnique();
        builder.HasOne(user => user.Role).WithMany().HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
