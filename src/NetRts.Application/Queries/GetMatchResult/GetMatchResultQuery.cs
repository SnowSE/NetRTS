using MediatR;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetMatchResult;

/// <summary>
/// Query to retrieve final match results for a completed match.
/// </summary>
public record GetMatchResultQuery(Guid MatchId) : IRequest<MatchResultResponse>;
