using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Endpoints;

/// <summary>
/// Block Bot strategies kept on a website account. The browser keeps its own copy of each and syncs it here
/// while someone is signed in (wwwroot/blocks/sync.js), so their strategies follow them to any computer.
/// Each save names the version it replaces, so two computers editing one strategy can't overwrite each other unseen.
/// </summary>
public static partial class AccountStrategyEndpoints
{
    /// <summary>The most strategies one account keeps.</summary>
    public const int MaxPerAccount = 100;

    /// <summary>The largest program kept, as JSON characters: far more than a 40-rule strategy needs.</summary>
    public const int MaxProgramChars = 64 * 1024;

    public static void MapAccountStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var strategies = app.MapGroup("/api/v1/account/strategies")
            .WithTags("Accounts (website)")
            .RequireAuthorization(Accounts.SignedInPolicy);

        strategies.MapGet("/", List)
            .Produces<List<AccountStrategyDto>>()
            .ProducesErrors()
            .WithSummary("Your Block Bot strategies saved to your account, newest change first.");

        strategies.MapPut("/{id}", Save)
            .Produces<AccountStrategyDto>()
            .Produces<AccountStrategyDto>(201)
            .Produces<AccountStrategyConflictDto>(409)
            .ProducesErrors()
            .WithSummary("Save a Block Bot strategy to your account. baseVersion is the version it replaces (null for a new one); " +
                         "if the account's copy has moved on, 409 STRATEGY_CHANGED returns it.");

        strategies.MapDelete("/{id}", Delete)
            .Produces(204)
            .Produces<AccountStrategyConflictDto>(409)
            .ProducesErrors()
            .WithSummary("Delete a strategy from your account. With ?version=N, only if it's still that version (else 409 STRATEGY_CHANGED).");
    }

    private static async Task<List<AccountStrategyDto>> List(ClaimsPrincipal user, NetRtsDb db, CancellationToken ct)
    {
        var accountId = user.AccountId();
        var rows = await db.AccountStrategies.AsNoTracking().Where(s => s.AccountId == accountId).ToListAsync(ct);
        return rows.OrderByDescending(s => s.UpdatedAt).Select(Dto).ToList();
    }

    private static async Task<IResult> Save(
        string id, SaveAccountStrategyRequest request, ClaimsPrincipal user, NetRtsDb db, TimeProvider time, CancellationToken ct)
    {
        if (!ValidId(id))
        {
            return InvalidId();
        }

        if (BlockProgramValidator.Validate(request.Program) is { } error)
        {
            return ApiErrors.Error(400, "INVALID_PROGRAM", error);
        }

        var json = JsonSerializer.Serialize(request.Program, NetRtsClient.JsonOptions);
        if (json.Length > MaxProgramChars)
        {
            return ApiErrors.Error(400, "PROGRAM_TOO_LARGE", $"A strategy can be at most {MaxProgramChars / 1024} KB; this one is {json.Length / 1024} KB.");
        }

        var accountId = user.AccountId();
        var row = await db.AccountStrategies.FirstOrDefaultAsync(s => s.AccountId == accountId && s.StrategyId == id, ct);
        if (row?.Version != request.BaseVersion)
        {
            return Changed(row);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var created = row is null;
        if (row is null)
        {
            if (await db.AccountStrategies.CountAsync(s => s.AccountId == accountId, ct) >= MaxPerAccount)
            {
                return ApiErrors.Error(409, "TOO_MANY_STRATEGIES", $"An account keeps at most {MaxPerAccount} strategies. Delete one you don't need first.");
            }

            row = new AccountStrategyRecord
            {
                AccountId = accountId,
                StrategyId = id,
                ProgramJson = json,
                Version = 1,
                CreatedAt = request.CreatedAt?.ToUniversalTime() is { } made && made <= now ? made : now,
                UpdatedAt = now,
            };
            db.AccountStrategies.Add(row);
        }
        else
        {
            row.ProgramJson = json;
            row.Version++;
            row.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another save got in first (the version moved on, or the same new id was added twice).
            db.ChangeTracker.Clear();
            return Changed(await FindAsync(db, accountId, id, ct));
        }

        return created ? Results.Created($"/api/v1/account/strategies/{id}", Dto(row)) : Results.Ok(Dto(row));
    }

    private static async Task<IResult> Delete(string id, int? version, ClaimsPrincipal user, NetRtsDb db, CancellationToken ct)
    {
        if (!ValidId(id))
        {
            return InvalidId();
        }

        var accountId = user.AccountId();
        var row = await db.AccountStrategies.FirstOrDefaultAsync(s => s.AccountId == accountId && s.StrategyId == id, ct);
        if (row is null)
        {
            return ApiErrors.NotFound("Strategy");
        }

        if (version is { } v && v != row.Version)
        {
            return Changed(row);
        }

        db.AccountStrategies.Remove(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Changed(await FindAsync(db, accountId, id, ct));
        }

        return Results.NoContent();
    }

    private static Task<AccountStrategyRecord?> FindAsync(NetRtsDb db, Guid accountId, string id, CancellationToken ct) =>
        db.AccountStrategies.AsNoTracking().FirstOrDefaultAsync(s => s.AccountId == accountId && s.StrategyId == id, ct);

    private static IResult Changed(AccountStrategyRecord? current) => Results.Json(new AccountStrategyConflictDto
    {
        Code = "STRATEGY_CHANGED",
        Message = current is null
            ? "That strategy isn't on your account any more (it was deleted somewhere else)."
            : $"That strategy changed somewhere else since you last saw it (it's on version {current.Version} now).",
        Current = current is null ? null : Dto(current),
    }, statusCode: 409);

    private static AccountStrategyDto Dto(AccountStrategyRecord s) => new()
    {
        Id = s.StrategyId,
        Program = JsonSerializer.Deserialize<BlockProgramDto>(s.ProgramJson, NetRtsClient.JsonOptions)!,
        Version = s.Version,
        CreatedAt = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(s.UpdatedAt, DateTimeKind.Utc),
    };

    private static bool ValidId(string id) => IdPattern().IsMatch(id);

    private static IResult InvalidId() =>
        ApiErrors.Error(400, "INVALID_STRATEGY_ID", "Strategy ids are 1-64 letters, digits, '-' or '_'.");

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex IdPattern();
}
