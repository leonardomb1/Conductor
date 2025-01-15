using Conductor.Model;
using Conductor.Shared.Config;
using LinqToDB;
using LinqToDB.Data;

namespace Conductor.Data;

public class LdbContext : DataConnection
{
    public LdbContext() : base(Settings.DbType) { }

    public ITable<Origin> Origins => this.GetTable<Origin>();

    public ITable<User> Users => this.GetTable<User>();

    public ITable<Schedule> Schedules => this.GetTable<Schedule>();

    public ITable<Extraction> Extractions => this.GetTable<Extraction>();

    public ITable<Destination> Destinations => this.GetTable<Destination>();

    public ITable<Record> Records => this.GetTable<Record>();
}