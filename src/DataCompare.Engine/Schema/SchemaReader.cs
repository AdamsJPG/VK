using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Reads table/view/column and function/stored-procedure metadata for a database using SQL Server
    /// catalog views (more reliable than INFORMATION_SCHEMA for identity/precision/PK details).
    /// </summary>
    public sealed class SchemaReader
    {
        // Views only differ from the tables query in their source object (sys.views vs sys.tables) and
        // the two columns tables have that views don't (a usable primary key, a real definition) — kept
        // as two separate query strings (rather than one dynamically built query) so both stay plain,
        // readable, static SQL, with the row-shape lined up so both can share one mapping loop below.
        private const string TablesQuery = """
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
                ISNULL(pk.key_ordinal, 0) AS PrimaryKeyOrdinal,
                CAST(NULL AS nvarchar(max)) AS Definition
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

        private const string ViewsQuery = """
            SELECT
                s.name AS SchemaName,
                v.name AS TableName,
                v.modify_date AS ModifiedAt,
                c.name AS ColumnName,
                ty.name AS DataType,
                c.max_length AS MaxLength,
                c.precision AS Precision,
                c.scale AS Scale,
                c.is_nullable AS IsNullable,
                c.is_identity AS IsIdentity,
                CAST(0 AS bit) AS IsPrimaryKey,
                CAST(0 AS tinyint) AS PrimaryKeyOrdinal,
                m.definition AS Definition
            FROM sys.views v
            JOIN sys.schemas s ON v.schema_id = s.schema_id
            JOIN sys.columns c ON c.object_id = v.object_id
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            LEFT JOIN sys.sql_modules m ON m.object_id = v.object_id
            ORDER BY s.name, v.name, c.column_id;
            """;

        // 'FN' = scalar function, 'IF' = inline table-valued function, 'TF' = multi-statement
        // table-valued function — all three are "functions" from this tool's point of view.
        private const string FunctionsQuery = """
            SELECT s.name AS SchemaName, o.name AS Name, o.modify_date AS ModifiedAt, m.definition AS Definition
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
            WHERE o.type IN ('FN', 'IF', 'TF')
            ORDER BY s.name, o.name;
            """;

        private const string StoredProceduresQuery = """
            SELECT s.name AS SchemaName, o.name AS Name, o.modify_date AS ModifiedAt, m.definition AS Definition
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
            WHERE o.type = 'P'
            ORDER BY s.name, o.name;
            """;

        /// <summary>
        /// Reads the table/column metadata for every table in the connected database — equivalent to
        /// calling <see cref="ReadSchemaAsync(SqlConnection, SchemaObjectTypes, CancellationToken)"/>
        /// with <see cref="SchemaObjectTypes.Tables"/>, kept for callers that only ever wanted tables.
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the read</param>
        /// <returns>returns a DataCompare.Engine.Schema.DatabaseSchema object containing every table and its columns</returns>
        public Task<DatabaseSchema> ReadSchemaAsync(SqlConnection connection, CancellationToken cancellationToken = default) =>
            ReadSchemaAsync(connection, SchemaObjectTypes.Tables, cancellationToken);

        /// <summary>
        /// Reads the full metadata for whichever object kinds are requested: tables and views (with
        /// their columns), and functions and stored procedures (with their definition text).
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="objectTypes">a DataCompare.Engine.Schema.SchemaObjectTypes flags value selecting which object kinds to read</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the read</param>
        /// <returns>returns a DataCompare.Engine.Schema.DatabaseSchema object containing every requested object kind</returns>
        public async Task<DatabaseSchema> ReadSchemaAsync(
            SqlConnection connection, SchemaObjectTypes objectTypes, CancellationToken cancellationToken = default)
        {
            var tables = objectTypes.HasFlag(SchemaObjectTypes.Tables)
                ? await ReadTableLikeObjectsAsync(connection, TablesQuery, SchemaObjectKind.Table, cancellationToken)
                : [];

            var views = objectTypes.HasFlag(SchemaObjectTypes.Views)
                ? await ReadTableLikeObjectsAsync(connection, ViewsQuery, SchemaObjectKind.View, cancellationToken)
                : [];

            var routines = new List<RoutineSchema>();
            if (objectTypes.HasFlag(SchemaObjectTypes.Functions))
            {
                routines.AddRange(await ReadRoutinesAsync(connection, RoutineKind.Function, cancellationToken));
            }

            if (objectTypes.HasFlag(SchemaObjectTypes.StoredProcedures))
            {
                routines.AddRange(await ReadRoutinesAsync(connection, RoutineKind.StoredProcedure, cancellationToken));
            }

            return new DatabaseSchema(tables) { Views = views, Routines = routines };
        }

        /// <summary>
        /// Runs one of the table-shaped queries (tables or views — same column shape, see <see
        /// cref="TablesQuery"/>/<see cref="ViewsQuery"/>) and groups its rows into one
        /// <see cref="TableSchema"/> per schema-qualified name.
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="query">a System.String holding the catalog-view query to run</param>
        /// <param name="kind">a DataCompare.Engine.Schema.SchemaObjectKind indicating whether the query's rows are tables or views</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the read</param>
        /// <returns>returns a System.Threading.Tasks.Task of a System.Collections.Generic.List of DataCompare.Engine.Schema.TableSchema, one per object read</returns>
        private static async Task<List<TableSchema>> ReadTableLikeObjectsAsync(
            SqlConnection connection, string query, SchemaObjectKind kind, CancellationToken cancellationToken)
        {
            var tables = new List<TableSchema>();
            string? currentTableKey = null;
            var currentSchemaName = string.Empty;
            var currentTableName = string.Empty;
            var currentModifiedAt = default(DateTime);
            string? currentDefinition = null;
            var currentColumns = new List<ColumnSchema>();

            await using var command = new SqlCommand(query, connection);
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
                        tables.Add(new TableSchema(currentSchemaName, currentTableName, currentColumns, currentModifiedAt, kind, currentDefinition));
                    }

                    currentTableKey = tableKey;
                    currentSchemaName = schemaName;
                    currentTableName = tableName;
                    currentModifiedAt = reader.GetDateTime(2);
                    currentDefinition = reader.IsDBNull(12) ? null : reader.GetString(12);
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
                tables.Add(new TableSchema(currentSchemaName, currentTableName, currentColumns, currentModifiedAt, kind, currentDefinition));
            }

            return tables;
        }

        /// <summary>
        /// Runs one of the routine queries (functions or stored procedures — same column shape, see
        /// <see cref="FunctionsQuery"/>/<see cref="StoredProceduresQuery"/>) and maps its rows to
        /// <see cref="RoutineSchema"/>.
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="kind">a DataCompare.Engine.Schema.RoutineKind indicating which query to run and to tag every resulting row with</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the read</param>
        /// <returns>returns a System.Threading.Tasks.Task of a System.Collections.Generic.List of DataCompare.Engine.Schema.RoutineSchema, one per routine read</returns>
        private static async Task<List<RoutineSchema>> ReadRoutinesAsync(
            SqlConnection connection, RoutineKind kind, CancellationToken cancellationToken)
        {
            var query = kind == RoutineKind.Function ? FunctionsQuery : StoredProceduresQuery;
            var routines = new List<RoutineSchema>();

            await using var command = new SqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                routines.Add(new RoutineSchema(
                    SchemaName: reader.GetString(0),
                    Name: reader.GetString(1),
                    ModifiedAt: reader.GetDateTime(2),
                    Definition: reader.IsDBNull(3) ? null : reader.GetString(3),
                    Kind: kind));
            }

            return routines;
        }
    }
}
