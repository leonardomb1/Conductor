using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Conductor.Model;

[Table("USERS")]
public sealed class User : IDbModel
{
    [Key]
    public uint Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("username")]
    public string Name { get; set; } = "";

    [Column]
    public string? Password { get; set; }
}