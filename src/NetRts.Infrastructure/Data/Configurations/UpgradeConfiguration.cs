using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

public class UpgradeConfiguration : IEntityTypeConfiguration<Upgrade>
{
    public void Configure(EntityTypeBuilder<Upgrade> builder)
    {
        builder.ToTable("Upgrades");
        builder.HasKey(u => new { u.MatchId, u.Id });

        builder.Property(u => u.UpgradeType).IsRequired().HasConversion<string>();
        builder.Property(u => u.OwnerId).IsRequired();
        builder.Property(u => u.IsCompleted).IsRequired();
        builder.Property(u => u.ResearchProgress).IsRequired();
        builder.Property(u => u.ResearchTicksRequired).IsRequired();
        builder.Property(u => u.ResourceCost).IsRequired();

        builder.HasIndex(u => new { u.MatchId, u.OwnerId })
            .HasDatabaseName("IX_Upgrades_Match_Owner");
    }
}
