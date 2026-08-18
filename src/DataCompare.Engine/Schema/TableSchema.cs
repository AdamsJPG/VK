namespace DataCompare.Engine.Schema;

public sealed record TableSchema(
    string SchemaName, string TableName, IReadOnlyList<ColumnSchema> Columns, DateTime ModifiedAt = default)
{
    public string FullName => $"{SchemaName}.{TableName}";

    public IReadOnlyList<ColumnSchema> PrimaryKeyColumnsInOrder =>
        Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.PrimaryKeyOrdinal).ToList();
}
