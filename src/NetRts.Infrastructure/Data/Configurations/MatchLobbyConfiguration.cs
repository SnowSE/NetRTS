using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;

namespace NetRts.Infrastructure.Data.Configurations;

public class MatchLobbyConfiguration : IEntityTypeConfiguration<MatchLobby>
{
    public void Configure(EntityTypeBuilder<MatchLobby> builder)
    {
        builder.ToTable("MatchLobbies");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Status).IsRequired().HasConversion<string>();

        builder.OwnsOne(l => l.GameSettings, settings =>
        {
            settings.Property(s => s.MapWidth).HasColumnName("MapWidth");
            settings.Property(s => s.MapHeight).HasColumnName("MapHeight");
            settings.Property(s => s.MaxTicks).HasColumnName("MaxTicks");
            settings.Property(s => s.TickIntervalMs).HasColumnName("TickIntervalMs");
            settings.Property(s => s.CommandQueueSize).HasColumnName("CommandQueueSize");
            settings.Property(s => s.CommandsPerTick).HasColumnName("CommandsPerTick");
            settings.Property(s => s.StartingResources).HasColumnName("StartingResources");
        });

        builder.Navigation(l => l.Players).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(l => l.Status).HasDatabaseName("IX_Lobbies_Status");
    }
}

public class LobbyPlayerConfiguration : IEntityTypeConfiguration<LobbyPlayer>
{
    public void Configure(EntityTypeBuilder<LobbyPlayer> builder)
    {
        builder.ToTable("LobbyPlayers");
        builder.HasKey(lp => new { lp.LobbyId, lp.PlayerId });
    }
}
