namespace DataCompare.Engine.Schema;

public sealed record ColumnChange(string ColumnName, ColumnSchema Source, ColumnSchema Target);
