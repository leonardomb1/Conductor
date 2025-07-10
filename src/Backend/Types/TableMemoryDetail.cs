namespace Conductor.Types;

public record TableMemoryDetail
{
    public string Identifier { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime LastAccessed { get; init; }
    public int RowCount { get; init; }
    public int ColumnCount { get; init; }
    public long EstimatedMemoryBytes { get; init; }
    public bool IsDisposed { get; init; }

    public double EstimatedMemoryMB => EstimatedMemoryBytes / (1024.0 * 1024.0);
    public TimeSpan Age => DateTime.UtcNow - CreatedAt;
    public TimeSpan TimeSinceLastAccess => DateTime.UtcNow - LastAccessed;
}