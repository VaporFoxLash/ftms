using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace FTMS.SharedKernel.Primitives;

/// <summary>
/// Minimal smart enum: a closed set of named, typed values declared as static readonly
/// fields on the derived type. Unlike a C# <c>enum</c> it can carry behaviour and a
/// non-integral key, and unlike a raw string or GUID it cannot hold a value nobody defined.
/// design: doc 02 section 6 - TransactionType (keyed by name) and TransactionStatus
/// (keyed by the seeded GUID) are both built on this.
/// </summary>
/// <typeparam name="TEnum">The derived smart enum type.</typeparam>
/// <typeparam name="TKey">The persisted key type: <c>string</c> or <c>Guid</c> here.</typeparam>
public abstract class SmartEnum<TEnum, TKey> : IEquatable<SmartEnum<TEnum, TKey>>
    where TEnum : SmartEnum<TEnum, TKey>
    where TKey : notnull
{
    private static readonly Lazy<IReadOnlyList<TEnum>> AllValues = new(DiscoverDeclaredValues);

    protected SmartEnum(string name, TKey value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Human readable name; this is what gets persisted for string keyed enums.</summary>
    public string Name { get; }

    /// <summary>The persisted key.</summary>
    public TKey Value { get; }

    /// <summary>Every value declared on the derived type, in declaration order.</summary>
    public static IReadOnlyList<TEnum> List => AllValues.Value;

    public static bool TryFromName(string? name, [NotNullWhen(true)] out TEnum? result)
    {
        result = name is null
            ? null
            : List.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        return result is not null;
    }

    public static bool TryFromValue(TKey? value, [NotNullWhen(true)] out TEnum? result)
    {
        result = value is null
            ? null
            : List.FirstOrDefault(item => EqualityComparer<TKey>.Default.Equals(item.Value, value));

        return result is not null;
    }

    public override string ToString() => Name;

    public bool Equals(SmartEnum<TEnum, TKey>? other) =>
        other is not null
        && other.GetType() == GetType()
        && EqualityComparer<TKey>.Default.Equals(other.Value, Value);

    public override bool Equals(object? obj) => obj is SmartEnum<TEnum, TKey> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Value);

    public static bool operator ==(SmartEnum<TEnum, TKey>? left, SmartEnum<TEnum, TKey>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SmartEnum<TEnum, TKey>? left, SmartEnum<TEnum, TKey>? right) =>
        !(left == right);

    /// <summary>
    /// Reads the public static readonly fields of the derived type once, on first use.
    /// This is the only reflection in the system outside DI registration.
    /// </summary>
    private static IReadOnlyList<TEnum> DiscoverDeclaredValues() =>
        typeof(TEnum)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(TEnum))
            .Select(field => (TEnum)field.GetValue(null)!)
            .ToList()
            .AsReadOnly();
}
