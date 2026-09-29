using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Code).HasMaxLength(32).IsRequired();
        builder.Property(role => role.Name).HasMaxLength(64).IsRequired();
        builder.HasIndex(role => role.Code).IsUnique();
        builder.HasData(
            new Role { Id = 1, Code = "Administrator", Name = "Administrador" },
            new Role { Id = 2, Code = "Attendant", Name = "Atendente" },
            new Role { Id = 3, Code = "Kitchen", Name = "Cozinha" },
            new Role { Id = 4, Code = "Dispatch", Name = "Expedição" });
    }
}
