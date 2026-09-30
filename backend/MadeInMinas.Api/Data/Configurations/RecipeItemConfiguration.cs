using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class RecipeItemConfiguration : IEntityTypeConfiguration<RecipeItem>
{
    public void Configure(EntityTypeBuilder<RecipeItem> builder)
    {
        builder.ToTable("RecipeItems", table =>
        {
            table.HasCheckConstraint("CK_RecipeItems_Quantity", "\"Quantity\" BETWEEN 0.001 AND 999999.999");
            table.HasCheckConstraint("CK_RecipeItems_Position", "\"Position\" BETWEEN 0 AND 99");
        });
        builder.HasKey(item => new { item.RecipeId, item.IngredientId });
        builder.Property(item => item.Quantity).HasPrecision(9, 3);
        builder.HasOne(item => item.Ingredient).WithMany().HasForeignKey(item => item.IngredientId).OnDelete(DeleteBehavior.Restrict);
    }
}
