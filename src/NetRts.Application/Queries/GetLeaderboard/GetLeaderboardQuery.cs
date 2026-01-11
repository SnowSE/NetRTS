using NetRts.Application.Abstractions;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetLeaderboard;

/// <summary>
/// Query to retrieve sorted leaderboard entries.
/// </summary>
public record GetLeaderboardQuery(
    int Page = 1,
    int PageSize = 20) : IQuery<LeaderboardResponse>;
