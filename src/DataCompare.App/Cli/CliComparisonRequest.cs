using DataCompare.Engine.Schema;

namespace DataCompare.App.Cli
{

    /// <summary>
    /// The full input to a CLI comparison run — deserialized from the JSON file path passed on the
    /// command line (see <see cref="CliRunner"/>).
    /// </summary>
    public sealed class CliComparisonRequest
    {
        /// <summary>which comparison(s) to run.</summary>
        public required CliComparisonMode Mode { get; set; }

        /// <summary>the source side's connection details.</summary>
        public required CliConnectionSpec Source { get; set; }

        /// <summary>the target side's connection details.</summary>
        public required CliConnectionSpec Target { get; set; }

        /// <summary>which object kinds to include — e.g. "tables, views". Omitted (null) means Tables
        /// alone, matching this tool's original table-only behavior so existing request files keep
        /// working unchanged. Functions/StoredProcedures are only meaningful when Mode is Schema or Both.</summary>
        public SchemaObjectTypes? ObjectTypes { get; set; }
    }
}
