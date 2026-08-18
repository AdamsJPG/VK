using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Schema;

/// <summary>
/// Reads table/column metadata for a database using SQL Server catalog views (more reliable than
/// INFORMATION_SCHEMA for identity/precision/PK details). Views only — stored procs/triggers/etc.
/// are out of scope.
/// </summary>
public sealed class SchemaReader
{
    private const string Query = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            t.modify_date AS ModifiedAt,
            c.name AS ColumnName,
            ty.name AS DataType,
            c.max_length AS MaxLength,
            c.precision AS Precision,
            c.scale AS Scale,
            c.is_nullable AS IsNullable,
            c.is_identity AS IsIdentity,
            CAST(CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsPrimaryKey,
            ISNULL(pk.key_ordinal, 0) AS PrimaryKeyOrdinal
        FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        JOIN sys.types ty ON c.user_type_id = ty.user_type_id
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id, ic.key_ordinal
            FROM sys.index_columns ic
            JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        ORDER BY s.name, t.name, c.column_id;
        """;

    public async Task<DatabaseSchema> ReadSchemaAsync(SqlConnection connection, CancellationToken cancellationToken = default)
    {
        var tables = new List<TableSchema>();
        string? currentTableKey = null;
        var currentSchemaName = string.Empty;
        var currentTableName = string.Empty;
        var currentModifiedAt = default(DateTime);
        var currentColumns = new List<ColumnSchema>();

        await using var command = new SqlCommand(Query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schemaName = reader.GetString(0);
            var tableName = reader.GetString(1);
            var tableKey = $"{schemaName}.{tableName}";

            if (tableKey != currentTableKey)
            {
                if (currentTableKey is not null)
                {
                    tables.Add(new TableSchema(currentSchemaName, currentTableName, currentColumns, currentModifiedAt));
                }

                currentTableKey = tableKey;
                currentSchemaName = schemaName;
                currentTableName = tableName;
                currentModifiedAt = reader.GetDateTime(2);
                currentColumns = new List<ColumnSchema>();
            }

            currentColumns.Add(new ColumnSchema(
                Name: reader.GetString(3),
                DataType: reader.GetString(4),
                MaxLength: reader.GetInt16(5),
                Precision: reader.GetByte(6),
                Scale: reader.GetByte(7),
                IsNullable: reader.GetBoolean(8),
                IsIdentity: reader.GetBoolean(9),
                IsPrimaryKey: reader.GetBoolean(10),
                PrimaryKeyOrdinal: reader.GetByte(11)));
        }

        if (currentTableKey is not null)
        {
            tables.Add(new TableSchema(currentSchemaName, currentTableName, currentColumns, currentModifiedAt));
        }

        return new DatabaseSchema(tables);
    }
}
