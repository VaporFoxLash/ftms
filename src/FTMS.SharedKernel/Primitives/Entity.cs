namespace FTMS.SharedKernel.Primitives;

/// <summary>
/// Base class for entities: identity is the GUID primary key, not the field values.
/// design: doc 02 - sequential GUID keys, so identity is assigned by the aggregate
/// factory rather than by the database.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id) => Id = id;

    /// <summary>Parameterless constructor for the EF Core materialiser only.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; protected set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override bool Equals(object? obj) => obj is Entity entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
