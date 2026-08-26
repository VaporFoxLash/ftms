namespace FTMS.Application.Abstractions;

/// <summary>
/// The response type for a command that succeeds without producing a value, so
/// <c>ICommand&lt;TResponse&gt;</c> needs no valueless twin and the dispatcher needs no
/// second code path. Update and Deactivate both return this.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
