using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MadeInMinas.Api.Data.Configurations;

public sealed class PrintStationConfiguration : IEntityTypeConfiguration<PrintStation>
{
    public void Configure(EntityTypeBuilder<PrintStation> builder)
    {
        builder.ToTable("PrintStations");
        builder.HasKey(station => station.Id);
        builder.Property(station => station.KeyHash).HasMaxLength(64);
        builder.HasData(new PrintStation { Id = 1 });
    }
}

public sealed class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("PrintJobs", table => table.HasCheckConstraint("CK_PrintJobs_State",
            "\"State\" IN ('Queued', 'Claimed', 'Submitted', 'Review', 'Cancelled')"));
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Mode).HasMaxLength(10);
        builder.Property(job => job.State).HasMaxLength(12);
        builder.Property(job => job.RequestKey).HasMaxLength(160);
        builder.HasIndex(job => job.RequestKey).IsUnique();
        builder.HasIndex(job => new { job.State, job.CreatedAt });
        builder.HasOne<Order>().WithMany().HasForeignKey(job => job.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
