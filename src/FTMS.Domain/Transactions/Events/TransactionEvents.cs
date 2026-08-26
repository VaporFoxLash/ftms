using FTMS.SharedKernel.Primitives;

namespace FTMS.Domain.Transactions.Events;

/// <summary>
/// design: doc 03 section 6 - these exist for business reactions (notify, integrate,
/// project), NOT for the compliance audit trail. The trail is written unconditionally by
/// the SaveChanges interceptor, because a trail that can be forgotten is not a trail.
/// No handlers are registered yet; the seam is what matters.
/// </summary>
public sealed record TransactionCreated(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record TransactionDetailsUpdated(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record TransactionHeld(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record TransactionReleased(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record TransactionCompleted(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record TransactionCancelled(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record TransactionDeactivated(Guid TransactionId, DateTime OccurredAtUtc) : IDomainEvent;
