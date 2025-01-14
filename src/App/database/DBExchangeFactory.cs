using LinqToDB;

namespace Conductor.App.Database;

public static class DBExchangeFactory
{
    public static DBExchange Create(string type)
    {
        return type switch
        {
            ProviderName.PostgreSQL => new PostgreSQLExchange(),
            ProviderName.MySql => new MySQLExchange(),
            ProviderName.SqlServer => new MSSQLExchange(),
            _ => throw new NotSupportedException($"Database type '{type}' is not supported")
        };
    }
}