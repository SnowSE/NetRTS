using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

public class MapTileConfiguration : IEntityTypeConfiguration<MapTile>
{
    public void Configure(EntityTypeBuilder<MapTile> builder)
    {
        builder.ToTable("MapTiles");

        // Use auto-increment ID as key for simplicity
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(t => t.MatchId).IsRequired();
        builder.Property(t => t.TerrainType).IsRequired().HasConversion<string>();

        builder.OwnsOne(t => t.Position, pos =>
        {
            pos.Property(p => p.X).HasColumnName("PositionX").IsRequired();
            pos.Property(p => p.Y).HasColumnName("PositionY").IsRequired();
        });

        // Relationship to Match
        builder.HasOne<Match>()
            .WithMany(m => m.MapTiles)
            .HasForeignKey(t => t.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Basic indices for performance
        builder.HasIndex(t => t.MatchId)
            .HasDatabaseName("IX_MapTiles_Match");
    }
}
