namespace PackageDeliverySystem.Infrastructure.Data;

public static class DatabaseSettings
{
    private const string ConnectionStringVariable = "PDS_CONNECTION_STRING";

    private const string DefaultConnectionString =
        "Server=localhost;Port=3306;Database=package_delivery_system;User=package_delivery_system_user;Password=package_delivery_system_password;";

    /// <summary>
    /// reads the mysql connection string from the environment, falling back to local docker defaults
    /// </summary>
    public static string GetConnectionString()
        => Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultConnectionString;
}
