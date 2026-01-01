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

        builder.Property(u => u.Type).IsRequired().HasConversion<string>();
        builder.Property(u => u.PlayerId).IsRequired();
        builder.Property(u => u.IsComplete).IsRequired();

        builder.HasIndex(u => new { u.MatchId, u.PlayerId })
            .HasDatabaseName("IX_Upgrades_Match_Player");
    }
}
