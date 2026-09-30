using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable("Recipes", table => table.HasCheckConstraint("CK_Recipes_YieldQuantity", "\"YieldQuantity\" BETWEEN 1 AND 10000"));
        builder.HasKey(recipe => recipe.Id);
        builder.Property(recipe => recipe.Instructions).HasMaxLength(2000);
        builder.HasIndex(recipe => recipe.ProductId).IsUnique();
        builder.HasOne(recipe => recipe.Product).WithOne().HasForeignKey<Recipe>(recipe => recipe.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(recipe => recipe.Items).WithOne(item => item.Recipe).HasForeignKey(item => item.RecipeId).OnDelete(DeleteBehavior.Cascade);
    }
}
