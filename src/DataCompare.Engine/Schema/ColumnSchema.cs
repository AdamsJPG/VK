namespace DataCompare.Engine.Schema;

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
