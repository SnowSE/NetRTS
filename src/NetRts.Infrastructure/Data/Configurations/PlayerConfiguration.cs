using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for Player entity.
/// </summary>
public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Username)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(p => p.Email)
            .HasMaxLength(100);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.LastActiveAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(p => p.Username)
            .IsUnique()
            .HasDatabaseName("IX_Players_Username");

        builder.HasIndex(p => p.Email)
            .HasDatabaseName("IX_Players_Email");

        builder.HasIndex(p => p.CurrentElo)
            .HasDatabaseName("IX_Players_Elo");
    }
}
