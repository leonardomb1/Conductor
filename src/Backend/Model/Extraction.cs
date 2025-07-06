using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("EXTRACTIONS")]
public sealed class Extraction : IDbModel
{
    [Key]
    public UInt32 Id { get; set; }

    [Column, Required, JsonRequired, JsonPropertyName("extractionName")]
    public string Name { get; set; } = "";

    [Column]
    public UInt32? ScheduleId { get; set; }

    [Column]
    public UInt32? OriginId { get; set; }

    [Column]
    public UInt32? DestinationId { get; set; }

    [Column]
    public string? IndexName { get; set; }

    [Column]
    public bool IsIncremental { get; set; } = false;

    [Column]
    public bool IsVirtual { get; set; } = false;

    [Column]
    public string? VirtualId { get; set; }

    [Column]
    public string? VirtualIdGroup { get; set; }

    [Column]
    public bool? IsVirtualTemplate { get; set; } = false;

    [Column]
    public string? FilterCondition { get; set; }

    [Column]
    public string? FilterColumn { get; set; }

    [Column]
    public Int32? FilterTime { get; set; }

    [Column]
    public string? OverrideQuery { get; set; }

    [Column, JsonPropertyName("extractionAlias")]
    public string? Alias { get; set; }

    [Column]
    public string? Dependencies { get; set; }

    [Column]
    public string? IgnoreColumns { get; set; }

    [Column]
    public string? HttpMethod { get; set; }

    [Column]
    public string? HeaderStructure { get; set; }

    [Column]
    public string? EndpointFullName { get; set; }

    [Column]
    public string? BodyStructure { get; set; }

    [Column]
    public string? OffsetAttr { get; set; }

    [Column]
    public string? OffsetLimitAttr { get; set; }

    [Column]
    public string? PageAttr { get; set; }

    [Column]
    public string? PaginationType { get; set; }

    [Column]
    public string? TotalPageAttr { get; set; }

    [Column]
    public string? SourceType { get; set; }

    [Column]
    public string? Script { get; set; }

    [NotMapped]
    public bool IsScriptBased => !string.IsNullOrEmpty(Script);

    [ForeignKey(nameof(ScheduleId))]
    public Schedule? Schedule { get; set; }

    [ForeignKey(nameof(DestinationId))]
    public Destination? Destination { get; set; }

    [ForeignKey(nameof(OriginId))]
    public Origin? Origin { get; set; }
}

public record SimpleExtractionDto(
    UInt32 Id,
    string Name
);

public record ExtractionAggregatedDto(
    UInt32 ExtractionId,
    string ExtractionName,
    int TotalJobs,
    float TotalSizeMB,
    DateTimeOffset? LastEndTime,
    int CompletedJobs,
    int FailedJobs,
    int RunningJobs
);