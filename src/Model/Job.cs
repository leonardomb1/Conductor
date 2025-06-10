using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("JOBS")]
public sealed class Job : IDbModel
{
    [Key]
    public Guid JobGuid { get; init; } = Guid.NewGuid();

    [Column]
    public JobType JobType { get; set; }

    [Column]
    public JobStatus Status { get; set; } = JobStatus.Running;

    [Column]
    public DateTime StartTime { get; set; } = DateTime.Now;

    [Column]
    public DateTime? EndTime { get; set; }

    [Column]
    public Int64 BytesAccumulated
    {
        get => Interlocked.Read(ref bytesAdded);
        set => Interlocked.Exchange(ref bytesAdded, value);
    }

    public List<JobExtraction> JobExtractions { get; set; } = [];

    [NotMapped]
    private Int64 bytesAdded;

    public void AddTransferedBytes(Int64 bytes) => Interlocked.Add(ref bytesAdded, bytes);
}

public enum JobStatus
{
    Running,
    Completed,
    Failed
}

public enum JobType
{
    Transfer,
    Fetch,
}

public record JobDto(
    string Name,
    Guid JobGuid,
    string JobType,
    string Status,
    DateTime StartTime,
    DateTime? EndTime,
    double TimeSpentMs,
    float Bytes
);