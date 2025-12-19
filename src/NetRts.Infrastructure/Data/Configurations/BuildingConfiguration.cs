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

        builder.OwnsMany(b => b.ProductionQueue, pq =>
        {
            pq.WithOwner().HasForeignKey("BuildingMatchId", "BuildingId");
            pq.Property<int>("Id");
            pq.HasKey("Id");
            pq.Property(p => p.UnitType).IsRequired().HasConversion<string>();
            pq.Property(p => p.TicksRemaining).IsRequired();
            pq.Property(p => p.TotalTicks).IsRequired();
        });

        builder.HasIndex(b => new { b.MatchId, b.OwnerId })
            .HasDatabaseName("IX_Buildings_Match_Owner");
    }
}
