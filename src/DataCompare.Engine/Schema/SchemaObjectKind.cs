namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The kind of table-like object a <see cref="TableSchema"/> represents — tables and views share
    /// the same shape (schema-qualified name, columns, queryable via a plain SELECT), so they're
    /// modeled with one type distinguished by this flag rather than two near-duplicate types.
    /// </summary>
    public enum SchemaObjectKind
    {
        /// <summary>a base table.</summary>
        Table,

        /// <summary>a view.</summary>
        View,
    }
}
