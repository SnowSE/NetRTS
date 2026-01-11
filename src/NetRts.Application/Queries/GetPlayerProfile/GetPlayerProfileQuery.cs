using NetRts.Application.Abstractions;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetPlayerProfile;

/// <summary>
/// Query to retrieve a player's profile.
/// </summary>
public record GetPlayerProfileQuery(Guid PlayerId) : IQuery<PlayerResponse>;
