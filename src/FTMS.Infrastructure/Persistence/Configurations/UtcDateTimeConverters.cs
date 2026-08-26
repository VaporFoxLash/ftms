using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FTMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stamps <see cref="DateTimeKind.Utc"/> on every timestamp read back from the database.
///
/// design: doc 02 section 1.4 - all timestamps are stored in UTC and the clients convert for
/// display. SQL Server's datetime2 carries no timezone, so EF materialises a DateTime with
/// Kind = Unspecified, and System.Text.Json then writes it WITHOUT a trailing Z. An Angular
/// client calling new Date("2026-08-20T09:30:00") reads that as local time, which in South
/// Africa is two hours wrong on every single record, silently.
///
/// Fixing it here rather than in the serializer means everything downstream is correct by
/// construction: comparisons, logging and JSON alike.
/// </summary>
internal static class UtcDateTimeConverters
{
    /// <summary>Writing is a no op: the domain already normalises to UTC before it saves.</summary>
    internal static readonly ValueConverter<DateTime, DateTime> Utc = new(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    internal static readonly ValueConverter<DateTime?, DateTime?> NullableUtc = new(
        value => value,
        value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
}
