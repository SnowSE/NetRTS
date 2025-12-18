using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NetRts.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating ApplicationDbContext during EF Core migrations.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Use default connection string for migrations
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=netrts;Username=postgres;Password=dev",
            b => b.MigrationsAssembly("NetRts.Infrastructure"));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
