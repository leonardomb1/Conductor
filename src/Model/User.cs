using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using System.Text.Json.Serialization;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class User : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "USERS";

    [PrimaryKey, Identity]
    [Key]
    public UInt32 Id { get; set; }

    [Column, NotNull, JsonRequired, JsonPropertyName("username")]
    public string Name { get; set; } = "";

    [Column, Nullable]
    public string? Password { get; set; }

    public User() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(User)));
    }
}