using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", table => table.HasCheckConstraint("CK_Customers_Phone",
            "\"Phone\" ~ '^\\+55[1-9][0-9]([2-5][0-9]{7}|9[0-9]{8})$'"));
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Name).HasMaxLength(120).IsRequired();
        builder.Property(customer => customer.NormalizedName).HasMaxLength(120).IsRequired();
        builder.Property(customer => customer.Phone).HasMaxLength(14).IsRequired();
        builder.HasIndex(customer => customer.Phone).IsUnique();
        builder.HasIndex(customer => new { customer.NormalizedName, customer.Id });
    }
}
