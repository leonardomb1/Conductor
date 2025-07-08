using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Conductor.Model;

[Table("DESTINATIONS")]
public sealed class Destination : IDbModel
{
    [Key]
    public uint Id { get; set; }

    [Column, JsonRequired, JsonPropertyName("destinationName")]
    public string Name { get; set; } = "";

    [Column, JsonRequired, JsonPropertyName("destinationDbType")]
    public string DbType { get; set; } = "";

    [Column, JsonRequired, JsonPropertyName("destinationConStr")]
    public string ConnectionString { get; set; } = "";

    [Column, JsonRequired, JsonPropertyName("destinationTimeZoneOffSet")]
    public int TimeZoneOffSet { get; set; }
}