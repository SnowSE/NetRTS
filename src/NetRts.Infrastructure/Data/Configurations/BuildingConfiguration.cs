using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

public class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("Buildings");
        builder.HasKey(b => new { b.MatchId, b.Id });

        builder.Property(b => b.Type).IsRequired().HasConversion<string>();
        builder.Property(b => b.OwnerId).IsRequired();

        builder.OwnsOne(b => b.Position, pos =>
        {
            pos.Property(p => p.X).HasColumnName("PositionX").IsRequired();
            pos.Property(p => p.Y).HasColumnName("PositionY").IsRequired();
        });

        // Configure ProductionQueue as owned collection
        builder.OwnsMany(b => b.ProductionQueue, po =>
        {
            po.Property(p => p.UnitType).IsRequired().HasConversion<string>();
            po.Property(p => p.TicksRemaining).IsRequired();
            po.Property(p => p.TotalTicks).IsRequired();
        });

        // Relationship to Match
        builder.HasOne<Match>()
            .WithMany(m => m.Buildings)
            .HasForeignKey(b => b.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => new { b.MatchId, b.OwnerId })
            .HasDatabaseName("IX_Buildings_Match_Owner");
    }
}
