using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class StoreAttendanceSettingsConfiguration : IEntityTypeConfiguration<StoreAttendanceSettings>
{
    public void Configure(EntityTypeBuilder<StoreAttendanceSettings> builder)
    {
        builder.ToTable("StoreAttendanceSettings", table =>
        {
            table.HasCheckConstraint("CK_StoreAttendanceSettings_Singleton", "\"Id\" = 1 AND \"Version\" > 0");
            table.HasCheckConstraint("CK_StoreAttendanceSettings_Settings", "jsonb_typeof(\"SettingsJson\") = 'object'");
        });
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.SettingsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(settings => settings.UpdatedByName).HasMaxLength(120).IsRequired();
    }
}
