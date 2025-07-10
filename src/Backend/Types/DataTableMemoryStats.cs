namespace Conductor.Types;

public record DataTableMemoryStats
{
    public int TotalActiveTables { get; init; }
    public long TotalEstimatedMemoryBytes { get; init; }
    public List<TableMemoryDetail> TableDetails { get; init; } = [];
    public SystemMemoryInfo SystemMemoryInfo { get; init; } = new();

    public double TotalEstimatedMemoryMB => TotalEstimatedMemoryBytes / (1024.0 * 1024.0);
}