using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("USERS")]
public sealed class User : IDbModel
{
    [Key]
    public UInt32 Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("username")]
    public string Name { get; set; } = "";

    [Column]
    public string? Password { get; set; }
}