using NetRts.Application.Abstractions;

namespace NetRts.Application.Commands.UpdateLeaderboard;

/// <summary>
/// Command to update leaderboard after a match completes.
/// </summary>
public record UpdateLeaderboardCommand(Guid MatchId) : ICommand;
