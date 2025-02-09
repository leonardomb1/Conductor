using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using Association = LinqToDB.Mapping.AssociationAttribute;
using Column = LinqToDB.Mapping.ColumnAttribute;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Http.Metadata;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class JobExtraction : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "JOBS_EXTRACTIONS";

    [PrimaryKey, Key, Identity]
    public UInt32 Id { get; set; }

    [ForeignKey(nameof(Job)), Column, NotNull]
    public Guid JobGuid { get; set; }

    [ForeignKey(nameof(Extraction)), Column, NotNull]
    public UInt32 ExtractionId { get; set; }

    [Association(ThisKey = nameof(JobGuid), OtherKey = nameof(Job.JobGuid))]
    public Job Job { get; set; } = null!;

    [Association(ThisKey = nameof(ExtractionId), OtherKey = nameof(Extraction.Id))]
    public Extraction Extraction { get; set; } = null!;

    public JobExtraction() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(JobExtraction)));
    }
}