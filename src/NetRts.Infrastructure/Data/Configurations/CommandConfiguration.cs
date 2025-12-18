using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

public class CommandConfiguration : IEntityTypeConfiguration<Command>
{
    public void Configure(EntityTypeBuilder<Command> builder)
    {
        builder.ToTable("Commands");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type).IsRequired().HasConversion<string>();
        builder.Property(c => c.Status).IsRequired().HasConversion<string>();
        builder.Property(c => c.TargetUnitIds).HasConversion(
            v => string.Join(',', v),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList());

        builder.OwnsOne(c => c.TargetPosition, pos =>
        {
            pos.Property(p => p.X).HasColumnName("TargetPositionX");
            pos.Property(p => p.Y).HasColumnName("TargetPositionY");
        });

        builder.Property(c => c.BuildingType).HasConversion<string>();
        builder.Property(c => c.UnitType).HasConversion<string>();
        builder.Property(c => c.UpgradeType).HasConversion<string>();

        builder.HasIndex(c => new { c.MatchId, c.PlayerId, c.Status })
            .HasDatabaseName("IX_Commands_Match_Player_Status");
    }
}
