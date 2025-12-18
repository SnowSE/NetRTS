using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

public class ResourceDepositConfiguration : IEntityTypeConfiguration<ResourceDeposit>
{
    public void Configure(EntityTypeBuilder<ResourceDeposit> builder)
    {
        builder.ToTable("ResourceDeposits");
        builder.HasKey(r => new { r.MatchId, r.Id });

        builder.OwnsOne(r => r.Position, pos =>
        {
            pos.Property(p => p.X).HasColumnName("PositionX").IsRequired();
            pos.Property(p => p.Y).HasColumnName("PositionY").IsRequired();
        });

        builder.Property(r => r.ResourceType).IsRequired().HasMaxLength(50);
        builder.Property(r => r.RemainingCapacity).IsRequired();
        builder.Property(r => r.InitialCapacity).IsRequired();

        builder.HasIndex(r => r.MatchId)
            .HasDatabaseName("IX_ResourceDeposits_Match");
    }
}
