namespace FTMS.SharedKernel.Primitives;

/// <summary>
/// Base class for value objects: equality is by value, never by reference or identity.
/// design: doc 02 section 6 - Money is the value object this exists for.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// The components that define this value. Two value objects with equal
    /// components in the same order are the same value.
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && other.GetType() == GetType()
        && other.GetEqualityComponents().SequenceEqual(GetEqualityComponents());

    public override bool Equals(object? obj) => obj is ValueObject value && Equals(value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
