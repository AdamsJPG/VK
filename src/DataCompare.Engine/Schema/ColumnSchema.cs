namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The schema definition of a single database column, as read from the source or target database.
    /// </summary>
    /// <param name="Name">a string containing the name of the column</param>
    /// <param name="DataType">a string containing the SQL data type of the column, e.g. varchar or int</param>
    /// <param name="MaxLength">a short containing the maximum storage length of the column, in bytes, as reported by SQL Server</param>
    /// <param name="Precision">a byte containing the numeric precision of the column</param>
    /// <param name="Scale">a byte containing the numeric scale of the column</param>
    /// <param name="IsNullable">a bool indicating whether the column allows null values</param>
    /// <param name="IsIdentity">a bool indicating whether the column is an identity column</param>
    /// <param name="IsPrimaryKey">a bool indicating whether the column participates in the table's primary key</param>
    /// <param name="PrimaryKeyOrdinal">an int containing the column's ordinal position within the primary key, or 0 when the column is not part of the primary key</param>
    public sealed record ColumnSchema(
        string Name,
        string DataType,
        short MaxLength,
        byte Precision,
        byte Scale,
        bool IsNullable,
        bool IsIdentity,
        bool IsPrimaryKey,
        int PrimaryKeyOrdinal = 0);
}
