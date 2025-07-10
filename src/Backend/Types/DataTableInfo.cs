namespace Conductor.Types;

public record DataTableMemoryInfo
{
    public string TableName { get; init; } = "";
    public int RowCount { get; init; }
    public int ColumnCount { get; init; }
    public long EstimatedBytes { get; init; }
    public long AccurateBytes { get; init; }
    public double EstimatedMB { get; init; }
    public double AccurateMB { get; init; }
    public string CalculationMethod { get; init; } = "";
}