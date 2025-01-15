using Conductor.Shared.Config;
using LinqToDB;
using LinqToDB.Configuration;

namespace Conductor.Data;

public class ProviderSettings : IConnectionStringSettings
{
    public string ConnectionString { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public bool IsGlobal => false;
}

public class ConnectionSettings : ILinqToDBSettings
{
    public IEnumerable<IDataProviderSettings> DataProviders => [];
    public string DefaultConfiguration => ProviderName.SQLite;
    public string DefaultDataProvider => ProviderName.SQLite;
    public IEnumerable<IConnectionStringSettings> ConnectionStrings
    {
        get
        {
            yield return
                new ProviderSettings
                {
                    Name = ProviderName.SQLite,
                    ProviderName = ProviderName.SQLite,
                    ConnectionString = Settings.ConnectionString
                };

            yield return
                new ProviderSettings
                {
                    Name = ProviderName.PostgreSQL,
                    ProviderName = ProviderName.PostgreSQL,
                    ConnectionString = Settings.ConnectionString
                };
        }
    }
}