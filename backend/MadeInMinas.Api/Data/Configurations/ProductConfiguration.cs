using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
            table.HasCheckConstraint("CK_Products_Price", "\"Price\" BETWEEN 0.01 AND 999999.99"));
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Name).HasMaxLength(120).IsRequired();
        builder.Property(product => product.NormalizedName).HasMaxLength(120).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(1000);
        builder.Property(product => product.ImageUrl).HasMaxLength(2048);
        builder.Property(product => product.Price).HasPrecision(8, 2);
        builder.HasIndex(product => new { product.CategoryId, product.NormalizedName }).IsUnique();
        builder.HasOne(product => product.Category).WithMany().HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

