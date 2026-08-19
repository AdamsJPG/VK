namespace DataCompare.App.ViewModels
{

    /// <summary>the kind of difference (or lack of one) between a source and target schema object.</summary>
    public enum SchemaObjectDiffKind
    {
        OnlyInSource,
        OnlyInTarget,
        Changed,
        Identical,
    }

    /// <summary>One table row in the schema results grid — mirrors SQL Compare's side-by-side object list.</summary>
    /// <param name="kind">a DataCompare.App.ViewModels.SchemaObjectDiffKind describing the kind of difference this row represents</param>
    /// <param name="groupLabel">a System.String used to group this row in the results grid</param>
    /// <param name="fullName">a System.String holding the table's fully-qualified name</param>
    /// <param name="sourceOwner">a System.String holding the schema (owner) name on the source side, or null if not present</param>
    /// <param name="sourceName">a System.String holding the table name on the source side, or null if not present</param>
    /// <param name="sourceModifiedAt">a System.DateTime holding the last-modified timestamp on the source side, or null if not present</param>
    /// <param name="targetOwner">a System.String holding the schema (owner) name on the target side, or null if not present</param>
    /// <param name="targetName">a System.String holding the table name on the target side, or null if not present</param>
    /// <param name="targetModifiedAt">a System.DateTime holding the last-modified timestamp on the target side, or null if not present</param>
    public sealed class SchemaObjectRow(
        SchemaObjectDiffKind kind,
        string groupLabel,
        string fullName,
        string? sourceOwner,
        string? sourceName,
        DateTime? sourceModifiedAt,
        string? targetOwner,
        string? targetName,
        DateTime? targetModifiedAt)
    {
        private const string Placeholder = "—";

        /// <summary>the kind of difference this row represents.</summary>
        public SchemaObjectDiffKind Kind { get; } = kind;

        /// <summary>Drives ListView grouping (e.g. "Only in Source") — see <see cref="MainWindowViewModel"/>.</summary>
        public string GroupLabel { get; } = groupLabel;

        /// <summary>the table's fully-qualified name.</summary>
        public string FullName { get; } = fullName;

        /// <summary>the display label for this row's object type — always "Table" for this grid.</summary>
        public string TypeLabel => "Table";

        /// <summary>the schema (owner) name on the source side, or null if the table doesn't exist there.</summary>
        public string? SourceOwner { get; } = sourceOwner;

        /// <summary>the table name on the source side, or null if the table doesn't exist there.</summary>
        public string? SourceName { get; } = sourceName;

        /// <summary>the last-modified timestamp on the source side, or null if the table doesn't exist there.</summary>
        public DateTime? SourceModifiedAt { get; } = sourceModifiedAt;

        /// <summary>the schema (owner) name on the target side, or null if the table doesn't exist there.</summary>
        public string? TargetOwner { get; } = targetOwner;

        /// <summary>the table name on the target side, or null if the table doesn't exist there.</summary>
        public string? TargetName { get; } = targetName;

        /// <summary>the last-modified timestamp on the target side, or null if the table doesn't exist there.</summary>
        public DateTime? TargetModifiedAt { get; } = targetModifiedAt;

        /// <summary>the source owner, or a placeholder dash when not present.</summary>
        public string SourceOwnerDisplay => SourceOwner ?? Placeholder;

        /// <summary>the source table name, or a placeholder dash when not present.</summary>
        public string SourceNameDisplay => SourceName ?? Placeholder;

        /// <summary>the target owner, or a placeholder dash when not present.</summary>
        public string TargetOwnerDisplay => TargetOwner ?? Placeholder;

        /// <summary>the target table name, or a placeholder dash when not present.</summary>
        public string TargetNameDisplay => TargetName ?? Placeholder;

        /// <summary>the source last-modified timestamp formatted for display, or a placeholder dash when not present.</summary>
        public string SourceModifiedDisplay => SourceModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? Placeholder;

        /// <summary>the target last-modified timestamp formatted for display, or a placeholder dash when not present.</summary>
        public string TargetModifiedDisplay => TargetModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? Placeholder;
    }
}
