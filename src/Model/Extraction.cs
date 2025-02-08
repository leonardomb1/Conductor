using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using Association = LinqToDB.Mapping.AssociationAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using Column = LinqToDB.Mapping.ColumnAttribute;
using System.Text.Json.Serialization;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http.Metadata;
using System.Reflection;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Extraction : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "EXTRACTIONS";

    [PrimaryKey, Identity]
    [Key]
    public UInt32 Id { get; set; }

    [Column, NotNull, JsonRequired, JsonPropertyName("extractionName")]
    public string Name { get; set; } = "";

    [Column, Nullable]
    public UInt32? ScheduleId { get; set; }

    [Column, NotNull, JsonRequired]
    public UInt32 OriginId { get; set; }

    [Column, Nullable]
    public UInt32? DestinationId { get; set; }

    [Column, NotNull, JsonRequired]
    public string IndexName { get; set; } = "";

    [Column, NotNull, JsonRequired]
    public bool IsIncremental { get; set; }

    [Column, NotNull, JsonRequired]
    public bool IsVirtual { get; set; }

    [Column, Nullable]
    public string? VirtualId { get; set; }

    [Column, Nullable]
    public string? VirtualIdGroup { get; set; }

    [Column, Nullable]
    public bool? IsVirtualTemplate { get; set; }

    [Column, NotNull, JsonRequired]
    public bool BeforeExecutionDeletes { get; set; }

    [Column, NotNull, JsonRequired]
    public bool SingleExecution { get; set; }

    [Column, Nullable]
    [AllowedValues("Columnar", "Relational")]
    public string? TableStructure { get; set; }

    [Column, Nullable]
    public string? FilterColumn { get; set; }

    [Column, Nullable]
    public Int32? FilterTime { get; set; }

    [Column, Nullable]
    public string? OverrideQuery { get; set; }

    [Column, Nullable, JsonPropertyName("extractionAlias")]
    public string? Alias { get; set; }

    [Column, Nullable]
    public string? Dependencies { get; set; }

    [Column, Nullable]
    public string? HttpMethod { get; set; }

    [Column, Nullable]
    public string? HeaderStructure { get; set; }

    [Column, Nullable]
    public string? EndpointFullName { get; set; }

    [Column, Nullable]
    public string? BodyStructure { get; set; }

    [Column, Nullable]
    public string? OffsetAttr { get; set; }

    [Column, Nullable]
    public string? OffsetLimitAttr { get; set; }

    [Column, Nullable]
    public string? PageAttr { get; set; }

    [Column, Nullable]
    public string? TotalPageAttr { get; set; }

    [Association(ThisKey = nameof(ScheduleId), OtherKey = nameof(Schedule.Id)), Nullable]
    [ForeignKey(nameof(ScheduleId))]
    public Schedule? Schedule { get; set; }

    [Association(ThisKey = nameof(DestinationId), OtherKey = nameof(Destination.Id)), Nullable]
    [ForeignKey(nameof(DestinationId))]
    public Destination? Destination { get; set; }

    [Association(ThisKey = nameof(OriginId), OtherKey = nameof(Origin.Id)), Nullable]
    [ForeignKey(nameof(OriginId))]
    public Origin? Origin { get; set; }

    public Extraction() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Extraction)));
    }
}