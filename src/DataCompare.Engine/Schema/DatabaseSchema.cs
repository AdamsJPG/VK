namespace DataCompare.Engine.Schema;

public sealed record DatabaseSchema(IReadOnlyList<TableSchema> Tables);
