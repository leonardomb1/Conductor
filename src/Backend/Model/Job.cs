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
    public DateTimeOffset StartTime { get; set; } = DateTimeOffset.UtcNow;

    [Column]
    public DateTimeOffset? EndTime { get; set; }

    public List<JobExtraction> JobExtractions { get; set; } = [];
}

public enum JobStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
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
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    double TimeSpentMs,
    float MegaBytes
);
