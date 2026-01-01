using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NetRts.Api.Endpoints;
using NetRts.Api.Hubs;
using NetRts.Api.Services;
using NetRts.Application.Behaviors;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Infrastructure.BackgroundServices;
using NetRts.Infrastructure.Caching;
using NetRts.Infrastructure.Data;
using NetRts.Infrastructure.Repositories;
using NetRts.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add database context
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase("TestDb"));
}
else
{
    builder.AddNpgsqlDbContext<ApplicationDbContext>("netrtsdb");
}

builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

// Add MediatR with behaviors
builder.Services.AddMediatR(config =>
{
    config.RegisterServicesFromAssembly(typeof(IApplicationDbContext).Assembly);
    config.AddOpenBehavior(typeof(ValidationBehavior<,>));
    config.AddOpenBehavior(typeof(LoggingBehavior<,>));
});

// Add FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(IApplicationDbContext).Assembly);

// Add repositories
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IMatchRepository, MatchRepository>();
builder.Services.AddScoped<IMatchLobbyRepository, MatchLobbyRepository>();
builder.Services.AddScoped<ILeaderboardRepository, LeaderboardRepository>();

// Add application services
builder.Services.AddSingleton<GameStateCache>();
builder.Services.AddSingleton<IGameStateCache>(sp => sp.GetRequiredService<GameStateCache>());
builder.Services.AddSingleton<ICommandQueueManager, CommandQueueManager>();
builder.Services.AddScoped<IFogOfWarCalculator, FogOfWarCalculator>();
builder.Services.AddScoped<IScoringService, ScoringService>();
builder.Services.AddScoped<IGameTickProcessor, GameTickProcessor>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IGameStateService, GameStateService>();
builder.Services.AddScoped<ICommandQueueService, CommandQueueService>();

// Add SignalR game update broadcaster (singleton because it uses IHubContext)
builder.Services.AddSingleton<IGameUpdateBroadcaster, SignalRGameUpdateBroadcaster>();

// Add background services
builder.Services.AddHostedService<GameTickService>();

// Add JWT authentication
var jwtSecret = builder.Configuration["JwtSettings:SecretKey"] ??
    "default-secret-key-for-development-only-min-32-chars";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "NetRts",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JwtSettings:Audience"] ?? "NetRts-Clients",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // In Testing environment, log authentication failures for debugging
        if (builder.Environment.IsEnvironment("Testing"))
        {
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogWarning("Authentication failed: {Exception}", context.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        }
    });

builder.Services.AddAuthorization();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "NetRts API v1");
    });
    app.UseWebAssemblyDebugging();
}

app.UseHttpsRedirection();

// Serve Blazor static files
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Map SignalR hub
app.MapHub<GameHub>("/hubs/game");

// Map health checks
app.MapHealthChecks("/health");

// Map API endpoints
app.MapGameEndpoints();
app.MapCommandEndpoints();
app.MapLobbyEndpoints();
app.MapPlayerEndpoints();
app.MapGet("/api", () => "NetRts API is running");

// Fallback to index.html for client-side routing
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

// Make Program accessible to tests
public partial class Program { }
