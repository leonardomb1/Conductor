using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using System.Text.Json.Serialization;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Origin : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "ORIGINS";

    [PrimaryKey, Identity]
    [Key]
    public UInt32 Id { get; set; }

    [Column, NotNull, JsonRequired, JsonPropertyName("originName")]
    public string Name { get; set; } = "";

    [Column, Nullable, JsonPropertyName("originAlias")]
    public string? Alias { get; set; }

    [Column, NotNull, JsonRequired, JsonPropertyName("originDbType")]
    public string DbType { get; set; } = "";

    [Column, NotNull, JsonRequired, JsonPropertyName("originConStr")]
    public string ConnectionString { get; set; } = "";

    public Origin() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Origin)));
    }
}