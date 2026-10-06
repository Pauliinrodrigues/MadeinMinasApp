using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class DeliverySettingsConfiguration : IEntityTypeConfiguration<DeliverySettings>
{
    public void Configure(EntityTypeBuilder<DeliverySettings> builder)
    {
        builder.ToTable("DeliverySettings", table =>
        {
            table.HasCheckConstraint("CK_DeliverySettings_Singleton", "\"Id\" = 1 AND \"Version\" > 0");
            table.HasCheckConstraint("CK_DeliverySettings_Areas", "jsonb_typeof(\"AreasJson\") = 'array' AND jsonb_array_length(\"AreasJson\") <= 500");
        });
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.AreasJson).HasColumnType("jsonb").IsRequired();
        builder.Property(settings => settings.UpdatedByName).HasMaxLength(120).IsRequired();
    }
}
