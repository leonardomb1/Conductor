using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("ORIGINS")]
public sealed class Origin : IDbModel
{
    [Key]
    public UInt32 Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("originName")]
    public string Name { get; set; } = "";

    [Column, JsonPropertyName("originAlias")]
    public string? Alias { get; set; }

    [Column, JsonPropertyName("originDbType")]
    public string? DbType { get; set; } = "";

    [Column, JsonPropertyName("originConStr")]
    public string? ConnectionString { get; set; } = "";

    [Column, JsonPropertyName("originTimeZoneOffSet")]
    public Int32? TimeZoneOffSet { get; set; }
}