using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NetRts.Server.Data;

/// <summary>Brings the database up to the latest migration at startup.</summary>
public static class DatabaseMigrator
{
    /// <summary>
    /// Databases created before migrations existed (by EnsureCreated) already have the tables of the first
    /// migration but no history. Those are marked as being at that first migration, so the later ones
    /// apply on top and existing players keep their API keys.
    /// </summary>
    public static async Task MigrateAsync(NetRtsDb db, ILogger logger, CancellationToken ct = default)
    {
        var history = db.GetService<IHistoryRepository>();
        if (!await TableExistsAsync(db, HistoryRepository.DefaultTableName, ct) && await TableExistsAsync(db, "players", ct))
        {
            var baseline = db.Database.GetMigrations().First();
            await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
            await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baseline, ProductInfo.GetVersion())), ct);
            logger.LogWarning("Database predates migrations; marked it as at {Migration}", baseline);
        }

        await db.Database.MigrateAsync(ct);
    }

    private static async Task<bool> TableExistsAsync(NetRtsDb db, string table, CancellationToken ct)
    {
        var sql = db.Database.IsSqlite()
            ? "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {0}"
            : "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = {0}";
        return await db.Database.SqlQueryRaw<int>(sql, table).SingleAsync(ct) > 0;
    }
}
