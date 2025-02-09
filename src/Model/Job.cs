using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Job : IDbModel
{
    private const string tableName = "JOBS";

    [Key, PrimaryKey]
    public UInt32 Id { get; set; }

    [Column, NotNull]
    public Guid JobGuid { get; init; } = Guid.NewGuid();

    [Column, NotNull]
    public JobTybe JobType { get; set; }

    [Column, NotNull]
    public JobStatus Status { get; set; } = JobStatus.Running;

    [Column, NotNull]
    public List<UInt32> ExtractionIds { get; init; } = [];

    [Column, NotNull]
    public DateTime StartTime { get; set; } = DateTime.Now;

    [Column, Nullable]
    public DateTime? EndTime { get; set; }

    [NotColumn]
    private Int64 bytesAdded;

    [Column, NotNull]
    public Int64 BytesAccumulated
    {
        get => Interlocked.Read(ref bytesAdded);
        set => Interlocked.Exchange(ref bytesAdded, value);
    }

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

public enum JobTybe
{
    Transfer,
    Fetch,
}
