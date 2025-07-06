namespace Conductor.Service.Database;

public static class DBExchangeFactory
{
    public static DBExchange Create(string type)
    {
        return type switch
        {
            "PostgreSQL" => new PostgreSQLExchange(),
            "MySql" => new MySQLExchange(),
            "SqlServer" => new MSSQLExchange(),
            _ => throw new NotSupportedException($"Database type '{type}' is not supported")
        };
    }
}