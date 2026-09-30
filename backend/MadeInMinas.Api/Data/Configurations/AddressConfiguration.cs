using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses", table =>
        {
            table.HasCheckConstraint("CK_Addresses_State", "\"State\" IN ('AC','AL','AP','AM','BA','CE','DF','ES','GO','MA','MT','MS','MG','PA','PB','PR','PE','PI','RJ','RN','RS','RO','RR','SC','SP','SE','TO')");
            table.HasCheckConstraint("CK_Addresses_PostalCode", "\"PostalCode\" IS NULL OR \"PostalCode\" ~ '^[0-9]{8}$'");
        });
        builder.HasKey(address => address.Id);
        builder.Property(address => address.Street).HasMaxLength(120).IsRequired();
        builder.Property(address => address.Number).HasMaxLength(20).IsRequired();
        builder.Property(address => address.Complement).HasMaxLength(120);
        builder.Property(address => address.Neighborhood).HasMaxLength(80).IsRequired();
        builder.Property(address => address.City).HasMaxLength(80).IsRequired();
        builder.Property(address => address.State).HasMaxLength(2).IsRequired();
        builder.Property(address => address.PostalCode).HasMaxLength(8);
        builder.Property(address => address.Reference).HasMaxLength(250);
        builder.HasOne<Customer>().WithMany().HasForeignKey(address => address.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(address => new { address.CustomerId, address.CreatedAt, address.Id });
    }
}
