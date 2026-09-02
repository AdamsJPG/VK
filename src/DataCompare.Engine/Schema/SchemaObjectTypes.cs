namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Which kinds of database object a schema read/compare should include — surfaced as checkboxes on
    /// the main screen (WPF) and as an optional field on the CLI request JSON, both defaulting to
    /// <see cref="Tables"/> alone to match this tool's original table-only behavior.
    /// </summary>
    [Flags]
    public enum SchemaObjectTypes
    {
        /// <summary>include nothing.</summary>
        None = 0,

        /// <summary>include base tables.</summary>
        Tables = 1,

        /// <summary>include views.</summary>
        Views = 2,

        /// <summary>include scalar/table-valued functions.</summary>
        Functions = 4,

        /// <summary>include stored procedures.</summary>
        StoredProcedures = 8,
    }
}
