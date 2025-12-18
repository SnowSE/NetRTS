using NetRts.Application.Abstractions;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetGameState;

/// <summary>
/// Query to retrieve game state for a specific player in a match.
/// </summary>
public record GetGameStateQuery(Guid MatchId, Guid PlayerId) : IQuery<GameStateResponse>;
