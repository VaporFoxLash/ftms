namespace FTMS.SharedKernel.Constants;

/// <summary>
/// The seeded AspNetRoles primary keys, fixed rather than generated, for the same reason
/// <see cref="TransactionStatusIds"/> is: HasData seeding has to be deterministic. A generated
/// GUID would make every `dotnet ef migrations add` emit a spurious delete-and-reinsert of the
/// role rows, and any user-to-role assignment made in one environment would dangle in another.
///
/// Changing any value here is a breaking data migration, not an edit.
/// </summary>
public static class FtmsRoleIds
{
    public static readonly Guid Capturer = Guid.Parse("b1b2c3d4-0001-4000-8000-000000000001");
    public static readonly Guid Manager = Guid.Parse("b1b2c3d4-0002-4000-8000-000000000002");
    public static readonly Guid Auditor = Guid.Parse("b1b2c3d4-0003-4000-8000-000000000003");
    public static readonly Guid Admin = Guid.Parse("b1b2c3d4-0004-4000-8000-000000000004");

    /// <summary>Role name to fixed id, in the order the roles are documented.</summary>
    public static readonly IReadOnlyDictionary<string, Guid> ByName =
        new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            [FtmsRoles.Capturer] = Capturer,
            [FtmsRoles.Manager] = Manager,
            [FtmsRoles.Auditor] = Auditor,
            [FtmsRoles.Admin] = Admin,
        };
}
