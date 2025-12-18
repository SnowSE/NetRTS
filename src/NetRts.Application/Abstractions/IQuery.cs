using MediatR;

namespace NetRts.Application.Abstractions;

/// <summary>
/// Marker interface for queries (read operations).
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
