namespace DataCompare.App.ViewModels;

public enum SchemaObjectDiffKind
{
    OnlyInSource,
    OnlyInTarget,
    Changed,
    Identical,
}

/// <summary>One table row in the schema results grid — mirrors SQL Compare's side-by-side object list.</summary>
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

    public SchemaObjectDiffKind Kind { get; } = kind;

    /// <summary>Drives ListView grouping (e.g. "Only in Source") — see <see cref="MainWindowViewModel"/>.</summary>
    public string GroupLabel { get; } = groupLabel;

    public string FullName { get; } = fullName;
    public string TypeLabel => "Table";
    public string? SourceOwner { get; } = sourceOwner;
    public string? SourceName { get; } = sourceName;
    public DateTime? SourceModifiedAt { get; } = sourceModifiedAt;
    public string? TargetOwner { get; } = targetOwner;
    public string? TargetName { get; } = targetName;
    public DateTime? TargetModifiedAt { get; } = targetModifiedAt;

    public string SourceOwnerDisplay => SourceOwner ?? Placeholder;
    public string SourceNameDisplay => SourceName ?? Placeholder;
    public string TargetOwnerDisplay => TargetOwner ?? Placeholder;
    public string TargetNameDisplay => TargetName ?? Placeholder;
    public string SourceModifiedDisplay => SourceModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? Placeholder;
    public string TargetModifiedDisplay => TargetModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? Placeholder;
}
