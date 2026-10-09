using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Server;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;
using NetRts.Server.Matches;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var options = builder.Configuration.GetSection(NetRtsOptions.Section).Get<NetRtsOptions>() ?? new NetRtsOptions();
builder.Services.Configure<NetRtsOptions>(builder.Configuration.GetSection(NetRtsOptions.Section));

// PostgreSQL when a "netrtsdb" connection string is supplied (Aspire locally, Azure, docker compose), SQLite otherwise.
// The Azure client uses the password when the connection string has one (local container) and
// Microsoft Entra ID via the app's managed identity when it doesn't (Azure Database for PostgreSQL).
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("netrtsdb")))
{
    builder.AddAzureNpgsqlDbContext<NetRtsDb>("netrtsdb");
}
else
{
    builder.Services.AddDbContext<NetRtsDb>(db =>
        db.UseSqlite(builder.Configuration.GetConnectionString("sqlite") ?? "Data Source=netrts.db"));
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ApiKeyCache>();
builder.Services.AddSingleton<HouseBotDirectory>();
builder.Services.AddSingleton<MatchMetrics>();
builder.Services.AddSingleton<MatchRecorder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchRecorder>());
builder.Services.AddSingleton<MatchManager>();
builder.Services.AddHostedService<MatchHousekeeper>();

builder.Services.AddAuthentication(ApiKeys.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeys.Scheme, null);
builder.Services.AddAuthorization();
builder.Services.AddNetRtsRateLimits(options);
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddNetRtsOpenApi();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 256 * 1024);

var app = builder.Build();

await InitializeDatabaseAsync(app.Services);

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Always revalidate so a redeploy never leaves spectators on stale JavaScript.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapNetRtsApiReference();
app.MapDefaultEndpoints();
app.MapPlayerEndpoints();
app.MapMatchEndpoints();
app.MapFallback("/api/{**path}", () => ApiErrors.Error(404, "NOT_FOUND", "No such API endpoint. See /scalar."));

await app.RunAsync();

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
    await db.Database.EnsureCreatedAsync();

    var directory = services.GetRequiredService<HouseBotDirectory>();
    foreach (var bot in HouseBots.All)
    {
        var name = HouseBotDirectory.PlayerName(bot.Name);
        var record = await db.Players.FirstOrDefaultAsync(p => p.Name == name);
        if (record is null)
        {
            record = new PlayerRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                IsHouseBot = true,
                ApiKeyHash = ApiKeys.Hash(ApiKeys.Generate()), // unusable: nobody ever sees this key
                CreatedAt = DateTime.UtcNow,
            };
            db.Players.Add(record);
        }

        directory.Register(bot.Name, record.Id);
    }

    await db.SaveChangesAsync();
}

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
