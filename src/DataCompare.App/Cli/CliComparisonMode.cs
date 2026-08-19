namespace DataCompare.App.Cli
{

    /// <summary>
    /// Which comparison(s) a CLI request JSON asks for. Required in every request rather than
    /// defaulting to "both" — a large database's schema compare is cheap but its data compare can take
    /// a long time, so silently always running both would surprise a caller who only wanted a quick
    /// schema check.
    /// </summary>
    public enum CliComparisonMode
    {
        /// <summary>compare schema only.</summary>
        Schema,

        /// <summary>compare data only.</summary>
        Data,

        /// <summary>compare both schema and data.</summary>
        Both,
    }
}
