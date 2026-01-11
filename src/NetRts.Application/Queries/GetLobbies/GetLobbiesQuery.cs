using NetRts.Application.Abstractions;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetLobbies;

/// <summary>
/// Query to retrieve a paginated list of open lobbies.
/// </summary>
public record GetLobbiesQuery(
    int Page = 1,
    int PageSize = 10) : IQuery<LobbyListResponse>;
