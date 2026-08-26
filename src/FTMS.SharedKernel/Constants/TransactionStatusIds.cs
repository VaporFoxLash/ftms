namespace FTMS.SharedKernel.Constants;

/// <summary>
/// The seeded TransactionStatuses primary keys, fixed rather than generated.
/// design: doc 02 section 4 - migrations must be deterministic so every environment
/// gets identical rows, the application can reference statuses without a lookup on every
/// request, and doc 07's filtered index can hard code the Active GUID in its WHERE clause.
/// Changing any value here is a breaking data migration, not an edit.
/// </summary>
public static class TransactionStatusIds
{
    public static readonly Guid Active = Guid.Parse("a1b2c3d4-0001-4000-8000-000000000001");
    public static readonly Guid Inactive = Guid.Parse("a1b2c3d4-0002-4000-8000-000000000002");
    public static readonly Guid Pending = Guid.Parse("a1b2c3d4-0003-4000-8000-000000000003");
    public static readonly Guid Completed = Guid.Parse("a1b2c3d4-0004-4000-8000-000000000004");
    public static readonly Guid Cancelled = Guid.Parse("a1b2c3d4-0005-4000-8000-000000000005");
}
