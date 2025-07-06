using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("SCHEDULES")]
public sealed class Schedule : IDbModel
{
    [Key]
    public UInt32 Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("scheduleName")]
    public string Name { get; set; } = "";

    [Column, JsonRequired]
    public bool Status { get; set; }

    [Column, JsonRequired]
    public Int32 Value { get; set; }
}