using FTMS.SharedKernel.Results;

namespace FTMS.Application.Abstractions;

/// <summary>
/// Resolves the one handler for a command or query and calls it, decorators and all.
/// design: doc 03 section 3 - controllers hold a dispatcher, not a pile of handler
/// dependencies, and the pipeline stays explicit and debuggable.
/// </summary>
public interface IDispatcher
{
    Task<Result<TResponse>> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default);

    Task<Result<TResponse>> Ask<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default);
}
