using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;

namespace NetRts.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for Match entity.
/// </summary>
public class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("Matches");

        builder.HasKey(m => m.Id);

        // Status and timing
        builder.Property(m => m.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(m => m.CurrentTick)
            .IsRequired();

        builder.Property(m => m.StartedAt);
        builder.Property(m => m.EndedAt);
        builder.Property(m => m.WinnerId);

        // Players
        builder.Property(m => m.Player1Id)
            .IsRequired();

        builder.Property(m => m.Player2Id)
            .IsRequired();

        // Owned complex types for scores
        builder.OwnsOne(m => m.Player1Score, score =>
        {
            score.Property(s => s.UnitsDestroyed).HasColumnName("Player1_UnitsDestroyed");
            score.Property(s => s.BuildingsDestroyed).HasColumnName("Player1_BuildingsDestroyed");
            score.Property(s => s.ResourcesGathered).HasColumnName("Player1_ResourcesGathered");
            score.Property(s => s.UnitsRemaining).HasColumnName("Player1_UnitsRemaining");
            score.Property(s => s.BuildingsRemaining).HasColumnName("Player1_BuildingsRemaining");
        });

        builder.OwnsOne(m => m.Player2Score, score =>
        {
            score.Property(s => s.UnitsDestroyed).HasColumnName("Player2_UnitsDestroyed");
            score.Property(s => s.BuildingsDestroyed).HasColumnName("Player2_BuildingsDestroyed");
            score.Property(s => s.ResourcesGathered).HasColumnName("Player2_ResourcesGathered");
            score.Property(s => s.UnitsRemaining).HasColumnName("Player2_UnitsRemaining");
            score.Property(s => s.BuildingsRemaining).HasColumnName("Player2_BuildingsRemaining");
        });

        // Resources
        builder.Property(m => m.Player1Resources).IsRequired();
        builder.Property(m => m.Player2Resources).IsRequired();

        // Helper properties to ignore
        builder.Ignore(m => m.Players);

        // Game state snapshot
        builder.Property(m => m.GameStateSnapshot);

        // Configure navigation properties using private backing fields
        builder.Navigation(m => m.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(m => m.Buildings).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(m => m.Commands).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(m => m.MapTiles).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(m => m.ResourceDeposits).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(m => m.Upgrades).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Indexes
        builder.HasIndex(m => m.Status)
            .HasDatabaseName("IX_Matches_Status");

        builder.HasIndex(m => new { m.Player1Id, m.Player2Id })
            .HasDatabaseName("IX_Matches_Players");

        builder.HasIndex(m => m.EndedAt)
            .HasDatabaseName("IX_Matches_EndedAt");
    }
}
