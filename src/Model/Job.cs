using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using Association = LinqToDB.Mapping.AssociationAttribute;
using PrimaryKey = LinqToDB.Mapping.PrimaryKeyAttribute;
using LinqToDB.Mapping;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Job : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "JOBS";

    [PrimaryKey, Key, NotNull]
    public Guid JobGuid { get; init; } = Guid.NewGuid();

    [Column, NotNull]
    public JobType JobType { get; set; }

    [Column, NotNull]
    public JobStatus Status { get; set; } = JobStatus.Running;

    [Column, NotNull]
    public DateTime StartTime { get; set; } = DateTime.Now;

    [Column, Nullable]
    public DateTime? EndTime { get; set; }

    [Column, NotNull]
    public Int64 BytesAccumulated
    {
        get => Interlocked.Read(ref bytesAdded);
        set => Interlocked.Exchange(ref bytesAdded, value);
    }

    [Association(ThisKey = nameof(JobGuid), OtherKey = nameof(JobExtraction.JobGuid))]
    public List<JobExtraction> JobExtractions { get; set; } = [];

    [NotColumn]
    private Int64 bytesAdded;

    public void AddTransferedBytes(Int64 bytes) => Interlocked.Add(ref bytesAdded, bytes);

    public Job() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Job)));
    }
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