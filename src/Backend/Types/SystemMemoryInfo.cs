namespace Conductor.Types;

public record SystemMemoryInfo
{
    public long ProcessWorkingSetBytes { get; init; }
    public long ProcessPrivateMemoryBytes { get; init; }
    public long GcTotalMemoryBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }

    public double ProcessWorkingSetMB => ProcessWorkingSetBytes / (1024.0 * 1024.0);
    public double ProcessPrivateMemoryMB => ProcessPrivateMemoryBytes / (1024.0 * 1024.0);
    public double GcTotalMemoryMB => GcTotalMemoryBytes / (1024.0 * 1024.0);
}