using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Conductor.Model;

[Table("SCHEDULES")]
public sealed class Schedule : IDbModel
{
    [Key]
    public uint Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("scheduleName")]
    public string Name { get; set; } = "";

    [Column, JsonRequired]
    public bool Status { get; set; }

    [Column, JsonRequired]
    public int Value { get; set; }
}