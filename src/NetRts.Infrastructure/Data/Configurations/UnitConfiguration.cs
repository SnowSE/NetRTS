using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for Unit entity.
/// </summary>
public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");

        builder.HasKey(u => new { u.MatchId, u.Id });

        builder.Property(u => u.Id)
            .IsRequired();

        builder.Property(u => u.OwnerId)
            .IsRequired();

        builder.Property(u => u.Type)
            .IsRequired()
            .HasConversion<string>();

        // Position as owned type
        builder.OwnsOne(u => u.Position, pos =>
        {
            pos.Property(p => p.X).HasColumnName("PositionX").IsRequired();
            pos.Property(p => p.Y).HasColumnName("PositionY").IsRequired();
        });

        builder.OwnsOne(u => u.TargetPosition, pos =>
        {
            pos.Property(p => p.X).HasColumnName("TargetPositionX");
            pos.Property(p => p.Y).HasColumnName("TargetPositionY");
        });

        builder.Property(u => u.CurrentStatus)
            .IsRequired()
            .HasConversion<string>();

        // Indexes for performance
        builder.HasIndex(u => new { u.MatchId, u.OwnerId })
            .HasDatabaseName("IX_Units_Match_Owner");
    }
}
