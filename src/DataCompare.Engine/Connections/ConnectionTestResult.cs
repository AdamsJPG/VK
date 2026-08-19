namespace DataCompare.Engine.Connections
{

    /// <summary>
    /// The result of testing a connection to a SQL Server endpoint.
    /// </summary>
    /// <param name="Success">a System.Boolean that is true when the connection attempt succeeded.</param>
    /// <param name="Message">a System.String describing the outcome of the connection attempt.</param>
    /// <param name="ServerVersion">a System.String containing the server version reported by SQL Server, or null when the connection failed.</param>
    public sealed record ConnectionTestResult(bool Success, string Message, string? ServerVersion = null)
    {
        /// <summary>
        /// creates a successful DataCompare.Engine.Connections.ConnectionTestResult for the given server version.
        /// </summary>
        /// <param name="serverVersion">a System.String containing the server version reported by SQL Server.</param>
        /// <returns>returns a DataCompare.Engine.Connections.ConnectionTestResult indicating success.</returns>
        public static ConnectionTestResult Ok(string serverVersion) =>
            new(true, "Connection succeeded.", serverVersion);

        /// <summary>
        /// creates a failed DataCompare.Engine.Connections.ConnectionTestResult with the given message.
        /// </summary>
        /// <param name="message">a System.String describing why the connection attempt failed.</param>
        /// <returns>returns a DataCompare.Engine.Connections.ConnectionTestResult indicating failure.</returns>
        public static ConnectionTestResult Failed(string message) =>
            new(false, message);
    }
}
