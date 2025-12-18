using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data.Configurations;

public class PlayerScoreConfiguration : IEntityTypeConfiguration<PlayerScore>
{
    public void Configure(EntityTypeBuilder<PlayerScore> builder)
    {
        builder.ToTable("PlayerScores");
        builder.HasKey(ps => ps.PlayerId);

        builder.Property(ps => ps.Username).IsRequired().HasMaxLength(20);
        builder.Property(ps => ps.WinRate).HasPrecision(5, 2);

        builder.HasIndex(ps => ps.TotalScore)
            .IsDescending()
            .HasDatabaseName("IX_PlayerScores_TotalScore");

        builder.HasIndex(ps => ps.Rank)
            .HasDatabaseName("IX_PlayerScores_Rank");
    }
}
