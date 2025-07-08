using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Conductor.Model;

[Table("ORIGINS")]
public sealed class Origin : IDbModel
{
    [Key]
    public uint Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("originName")]
    public string Name { get; set; } = "";

    [Column, JsonPropertyName("originAlias")]
    public string? Alias { get; set; }

    [Column, JsonPropertyName("originDbType")]
    public string? DbType { get; set; } = "";

    [Column, JsonPropertyName("originConStr")]
    public string? ConnectionString { get; set; } = "";

    [Column, JsonPropertyName("originTimeZoneOffSet")]
    public int? TimeZoneOffSet { get; set; }
}