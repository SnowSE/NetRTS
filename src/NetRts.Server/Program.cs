using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Server;
using NetRts.Server.Achievements;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;
using NetRts.Server.Matches;
using NetRts.Server.Recaps;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var options = builder.Configuration.GetSection(NetRtsOptions.Section).Get<NetRtsOptions>() ?? new NetRtsOptions();
builder.Services.Configure<NetRtsOptions>(builder.Configuration.GetSection(NetRtsOptions.Section));

// PostgreSQL when a "netrtsdb" connection string is supplied (Aspire locally, Azure, docker compose), SQLite otherwise.
// The Azure client uses the password when the connection string has one (local container) and
// Microsoft Entra ID via the app's managed identity when it doesn't (Azure Database for PostgreSQL).
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("netrtsdb")))
{
    builder.AddAzureNpgsqlDbContext<PostgresNetRtsDb>("netrtsdb");
    builder.Services.AddScoped<NetRtsDb>(sp => sp.GetRequiredService<PostgresNetRtsDb>());
}
else
{
    builder.Services.AddDbContext<SqliteNetRtsDb>(db =>
        db.UseSqlite(builder.Configuration.GetConnectionString("sqlite") ?? "Data Source=netrts.db"));
    builder.Services.AddScoped<NetRtsDb>(sp => sp.GetRequiredService<SqliteNetRtsDb>());
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ApiKeyCache>();
builder.Services.AddSingleton<HouseBotDirectory>();
builder.Services.AddSingleton<MatchMetrics>();
builder.Services.AddHttpClient<IDiscordWebhook, DiscordWebhook>();
builder.Services.AddSingleton<MatchAnnouncer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchAnnouncer>());
builder.Services.Configure<RecapOptions>(builder.Configuration.GetSection(RecapOptions.Section));
builder.Services.AddSingleton<LadderSnapshots>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LadderSnapshots>());
builder.Services.AddScoped<RecapBrief>();
builder.Services.AddSingleton<RecapVideos>();
builder.Services.AddHostedService<RecapLibrary>();
// Recap videos live in blob storage: Azurite under Aspire, an Azure storage account in production
// (ConnectionStrings__recaps = https://<account>.blob.core.windows.net/, signed in with the app's identity).
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("recaps")))
{
    builder.AddAzureBlobServiceClient("recaps");
}

builder.Services.AddSingleton<AchievementService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AchievementService>());
builder.Services.AddSingleton<MatchRecorder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchRecorder>());
builder.Services.AddSingleton<MatchManager>();
builder.Services.AddSingleton<LeagueMatchManager>();
builder.Services.AddSingleton<ReplayFrameStore>();
builder.Services.AddSingleton<Presence>();
builder.Services.AddHostedService<MatchHousekeeper>();

// Bots authenticate with API keys (the default scheme); people sign in to the website with Discord.
var discord = builder.Configuration.GetSection(DiscordOptions.Section).Get<DiscordOptions>() ?? new DiscordOptions();
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.Section));
var auth = builder.Services.AddAuthentication(ApiKeys.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeys.Scheme, null);
builder.Services.AddNetRtsAccounts(auth, discord);
builder.Services.AddNetRtsRateLimits(options);
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddNetRtsOpenApi();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = ApiErrors.MaxRequestBodyBytes);
// Brotli or gzip for the site's JavaScript, CSS, JSON and HTML: three.js alone shrinks from 690 KB
// to under 200 KB, which is most of what the 3D view waits for on a slow connection.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["text/javascript", "image/svg+xml"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Optimal);

var app = builder.Build();

await InitializeDatabaseAsync(app.Services);

app.UseApiErrors();
app.UseResponseCompression();
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
app.MapAccountEndpoints();
app.MapAchievementEndpoints();
app.MapPeopleEndpoints();
app.MapRecapEndpoints();
app.MapFallback("/api/{**path}", () => ApiErrors.Error(404, "NOT_FOUND", "No such API endpoint. See /scalar."));

await app.RunAsync();

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
    await DatabaseMigrator.MigrateAsync(db, scope.ServiceProvider.GetRequiredService<ILogger<NetRtsDb>>());

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
