namespace DataCompare.Engine.Connections;

public sealed record ConnectionTestResult(bool Success, string Message, string? ServerVersion = null)
{
    public static ConnectionTestResult Ok(string serverVersion) =>
        new(true, "Connection succeeded.", serverVersion);

    public static ConnectionTestResult Failed(string message) =>
        new(false, message);
}
