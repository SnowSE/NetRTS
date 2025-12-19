using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NetRts.Api.Endpoints;
using NetRts.Api.Hubs;
using NetRts.Api.Middleware;
using NetRts.Application.Behaviors;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Infrastructure.BackgroundServices;
using NetRts.Infrastructure.Caching;
using NetRts.Infrastructure.Data;
using NetRts.Infrastructure.Repositories;
using NetRts.Infrastructure.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for structured logging
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "NetRts.Api")
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/netrts-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
});

// Add database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection") ??
        "Host=localhost;Database=netrts;Username=postgres;Password=dev"));

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
builder.Services.AddSingleton<IGameTickProcessor, GameTickProcessor>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// Add background services
builder.Services.AddHostedService<GameTickService>();
builder.Services.AddHostedService<MatchSnapshotService>();

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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// Configure middleware pipeline
app.UseExceptionHandlingMiddleware();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "NetRts API v1");
    });
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthenticationMiddleware();
app.UseAuthorization();
app.UseRateLimitingMiddleware();

// Map SignalR hub
app.MapHub<GameHub>("/hubs/game");

// Map health checks
app.MapHealthChecks("/health");

// Map API endpoints
app.MapGameEndpoints();
app.MapCommandEndpoints();
app.MapGet("/", () => "NetRts API is running");

app.Run();
