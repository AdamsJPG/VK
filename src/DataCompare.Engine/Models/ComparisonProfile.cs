namespace DataCompare.Engine.Models
{

    /// <summary>
    /// A saved comparison setup: the two connections to diff. Exclusion rules are added in a later phase.
    /// </summary>
    public sealed class ComparisonProfile
    {
        /// <summary>the display name of this comparison profile.</summary>
        public required string Name { get; set; }

        /// <summary>the connection profile for side A of the comparison.</summary>
        public required ConnectionProfile ConnectionA { get; set; }

        /// <summary>the connection profile for side B of the comparison.</summary>
        public required ConnectionProfile ConnectionB { get; set; }
    }
}
