using DataCompare.Engine.Connections;
using DataCompare.Engine.Models;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Tests.Connections;

public sealed class SqlConnectionFactoryTests
{
    [Fact]
    public void BuildConnectionString_UsesSuppliedPassword()
    {
        var profile = new ConnectionProfile
        {
            Name = "A",
            ServerName = "srv-a",
            DatabaseName = "AppDb",
            UserId = "user_a",
        };
        var factory = new SqlConnectionFactory();

        var connectionString = factory.BuildConnectionString(profile, "s3cr3t");

        var builder = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("srv-a", builder.DataSource);
        Assert.Equal("AppDb", builder.InitialCatalog);
        Assert.Equal("user_a", builder.UserID);
        Assert.Equal("s3cr3t", builder.Password);
    }
}
