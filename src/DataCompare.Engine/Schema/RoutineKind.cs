namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The kind of routine a <see cref="RoutineSchema"/> represents.
    /// </summary>
    public enum RoutineKind
    {
        /// <summary>a scalar, inline table-valued, or multi-statement table-valued function.</summary>
        Function,

        /// <summary>a stored procedure.</summary>
        StoredProcedure,
    }
}
