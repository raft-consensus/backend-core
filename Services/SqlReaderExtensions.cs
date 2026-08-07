using System.Data.Common;

namespace raft_backend.Services;

public static class SqlReaderExtensions
{
    private static bool HasColumn(DbDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetStringOrEmpty(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return string.Empty;
        var value = reader[name];
        return value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    public static string? GetNullableString(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return null;
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    public static int GetInt32Value(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return 0;
        var value = reader[name];
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    public static int? GetNullableInt32(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return null;
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    public static long GetInt64Value(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return 0L;
        var value = reader[name];
        return value == DBNull.Value ? 0L : Convert.ToInt64(value);
    }

    public static long? GetNullableInt64(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return null;
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    public static DateTime GetDateTimeValue(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return DateTime.MinValue;
        var value = reader[name];
        return value == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(value);
    }

    public static DateTime? GetNullableDateTime(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return null;
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    public static decimal GetDecimalValue(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return 0m;
        var value = reader[name];
        return value == DBNull.Value ? 0m : Convert.ToDecimal(value);
    }

    public static bool GetBooleanValue(this DbDataReader reader, string name)
    {
        if (!HasColumn(reader, name)) return false;
        var value = reader[name];
        return value == DBNull.Value ? false : Convert.ToBoolean(value);
    }
}
