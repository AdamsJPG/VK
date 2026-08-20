namespace DataCompare.Engine.Reporting
{

    /// <summary>Which category of data difference a <see cref="DataComparisonDetailNode"/> container
    /// represents — set only on the top-level "Rows only in Source/Target", "Rows with reassigned key",
    /// and "Rows with changed values" container nodes built for a keyed comparison, so the HTML report's
    /// "Accept" feature can identify which summary count column an accepted example belongs to without
    /// resorting to fragile text-matching on the container's heading.</summary>
    public enum DataComparisonRowCategory
    {
        /// <summary>Rows present in the source table but missing from the target table.</summary>
        OnlyInSource,

        /// <summary>Rows present in the target table but missing from the source table.</summary>
        OnlyInTarget,

        /// <summary>Rows found on both sides with identical non-key values but a different key.</summary>
        ReassignedKey,

        /// <summary>Rows whose key matched on both sides but whose non-key values differ.</summary>
        Changed,
    }
}
