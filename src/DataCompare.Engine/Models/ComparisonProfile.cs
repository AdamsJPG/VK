namespace DataCompare.Engine.Models;

/// <summary>
/// A saved comparison setup: the two connections to diff. Exclusion rules are added in a later phase.
/// </summary>
public sealed class ComparisonProfile
{
    public required string Name { get; set; }
    public required ConnectionProfile ConnectionA { get; set; }
    public required ConnectionProfile ConnectionB { get; set; }
}
