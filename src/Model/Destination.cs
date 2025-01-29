using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using System.Text.Json.Serialization;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.Metadata;
using System.Reflection;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Destination : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "DESTINATIONS";

    [PrimaryKey, Identity]
    [Key]
    public UInt32 Id { get; set; }

    [Column, NotNull, JsonRequired, JsonPropertyName("destinationName")]
    public string Name { get; set; } = "";

    [Column, NotNull, JsonRequired, JsonPropertyName("destinationDbType")]
    public string DbType { get; set; } = "";

    [Column, NotNull, JsonRequired, JsonPropertyName("destinationConStr")]
    public string ConnectionString { get; set; } = "";

    [Column, NotNull, JsonRequired, JsonPropertyName("destinationTimeZoneOffSet")]
    public double TimeZoneOffSet { get; set; }

    public Destination() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Destination)));
    }
}