namespace FTMS.Application.TransactionStatuses;

/// <summary>
/// design: doc 05 section 2 - the whole set, no paging, effectively immutable, which also
/// makes it the perfect cache warm up call for clients.
/// </summary>
public sealed record TransactionStatusDto(Guid Id, string StatusName);
