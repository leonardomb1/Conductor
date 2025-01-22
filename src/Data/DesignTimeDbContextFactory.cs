using Conductor.Shared;
using Microsoft.EntityFrameworkCore.Design;

namespace Conductor.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfContext>
{
    public EfContext CreateDbContext(string[] args)
    {
        Initializer.InitializeFromFile(args[0]);
        return new EfContext();
    }
}
