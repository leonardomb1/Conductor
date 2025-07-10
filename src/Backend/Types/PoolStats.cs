namespace Conductor.Types;

public record PoolStats
{
    public int ActiveConnections { get; init; }
    public int IdleConnections { get; init; }
    public int PoolSize { get; init; }
    public int CreatedConnections { get; init; }
    public DateTimeOffset LastActivity { get; init; }
}