namespace Conductor.Types;

public record ConnectionPoolStats
{
    public int TotalActivePools { get; init; }
    public int TotalActiveConnections { get; init; }
    public int TotalPoolSize { get; init; }
    public List<PoolDetail> PoolDetails { get; init; } = new();
}
