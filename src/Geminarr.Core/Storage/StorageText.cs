using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Storage;

/// <summary>Moments as text (round-trip, UTC) and the reading of a nullable column: the same in every store.</summary>
internal static class StorageText
{
    public static string Moment(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static string? Moment(DateTimeOffset? value) => value is { } moment ? Moment(moment) : null;

    public static DateTimeOffset ReadMoment(SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? ReadNullableMoment(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadMoment(reader, ordinal);

    public static string? ReadNullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int? ReadNullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static object Value(object? value) => value ?? DBNull.Value;
}
