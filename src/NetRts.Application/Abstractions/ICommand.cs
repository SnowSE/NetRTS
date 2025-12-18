using MediatR;

namespace NetRts.Application.Abstractions;

/// <summary>
/// Marker interface for commands (mutations).
/// </summary>
public interface ICommand : IRequest
{
}

/// <summary>
/// Marker interface for commands that return a result.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}
