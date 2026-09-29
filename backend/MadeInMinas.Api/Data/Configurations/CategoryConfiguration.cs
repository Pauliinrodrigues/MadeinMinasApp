using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", table =>
            table.HasCheckConstraint("CK_Categories_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 9999"));
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Name).HasMaxLength(80).IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(80).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(500);
        builder.HasIndex(category => category.NormalizedName).IsUnique();
    }
}

