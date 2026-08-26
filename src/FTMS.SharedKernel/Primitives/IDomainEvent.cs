namespace FTMS.SharedKernel.Primitives;

/// <summary>
/// A business fact that has already happened inside the domain.
/// design: doc 03 section 6 - domain events are for business reactions
/// (notify, integrate, project). The compliance audit trail is NOT carried by
/// events; it is written unconditionally by the SaveChanges interceptor,
/// because a trail that depends on someone remembering to raise an event is not a trail.
/// </summary>
public interface IDomainEvent
{
    /// <summary>UTC instant the fact occurred. All timestamps in FTMS are UTC.</summary>
    DateTime OccurredAtUtc { get; }
}
