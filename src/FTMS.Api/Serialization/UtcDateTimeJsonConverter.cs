using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FTMS.Api.Serialization;

/// <summary>
/// Guarantees every timestamp on the wire carries the UTC designator.
///
/// design: doc 05 section 1 - all timestamps in UTC using ISO 8601, which means the trailing Z
/// is part of the contract and not a formatting preference. The EF value converters already
/// stamp Kind = Utc on everything read from the database; this is the backstop for any
/// DateTime that reaches a response from somewhere else, because a timestamp without an offset
/// is silently reinterpreted as local time by every browser that parses it.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    private const string Iso8601Utc = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),

            // Unspecified is treated as already UTC rather than converted, because everything
            // FTMS stores is UTC. Converting would shift correct values by the server's offset.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        writer.WriteStringValue(utc.ToString(Iso8601Utc, CultureInfo.InvariantCulture));
    }
}

/// <summary>The nullable twin, since System.Text.Json treats DateTime? as its own type.</summary>
public sealed class NullableUtcDateTimeJsonConverter : JsonConverter<DateTime?>
{
    private static readonly UtcDateTimeJsonConverter Inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(DateTime), options);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        Inner.Write(writer, value.Value, options);
    }
}
