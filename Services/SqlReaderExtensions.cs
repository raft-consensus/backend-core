using System.Data.Common;

namespace raft_backend.Services;

public static class SqlReaderExtensions
{
    public static string GetStringOrEmpty(this DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    public static string? GetNullableString(this DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    public static int GetInt32Value(this DbDataReader reader, string name)
    {
        return Convert.ToInt32(reader[name]);
    }

    public static int? GetNullableInt32(this DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    public static long GetInt64Value(this DbDataReader reader, string name)
    {
        return Convert.ToInt64(reader[name]);
    }

    public static long? GetNullableInt64(this DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    public static DateTime GetDateTimeValue(this DbDataReader reader, string name)
    {
        return Convert.ToDateTime(reader[name]);
    }

    public static DateTime? GetNullableDateTime(this DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    public static decimal GetDecimalValue(this DbDataReader reader, string name)
    {
        return Convert.ToDecimal(reader[name]);
    }

    public static bool GetBooleanValue(this DbDataReader reader, string name)
    {
        return Convert.ToBoolean(reader[name]);
    }
}
