using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Reporting
{

    /// <summary>
    /// Describes the on-demand "open both" drill-down available for one changed large-content
    /// (MAX-length varbinary) column value. The comparison pass keeps only a hash for these columns
    /// (see <see cref="DataComparison.LargeContentColumn"/>), so re-fetching the real bytes on demand
    /// needs the table schema and row key to look the value back up — a plain Engine-layer DTO so
    /// both the App layer (wiring an on-screen click action) and the CLI can consume it without the
    /// Engine project depending on either one's UI concerns.
    /// </summary>
    /// <param name="SourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
    /// <param name="TargetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
    /// <param name="ColumnName">a System.String holding the name of the changed large-content column</param>
    /// <param name="KeyValues">a System.Collections.Generic.IReadOnlyDictionary of System.String to nullable System.Object holding the row's primary-key column name/value pairs, used to re-locate it</param>
    public sealed record DataComparisonLargeContentAction(
        TableSchema SourceTable,
        TableSchema TargetTable,
        string ColumnName,
        IReadOnlyDictionary<string, object?> KeyValues);
}
