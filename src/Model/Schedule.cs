using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using System.Text.Json.Serialization;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Schedule : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "SCHEDULES";

    [PrimaryKey, Identity]
    [Key]
    public UInt32 Id { get; set; }

    [Column, NotNull, JsonRequired, JsonPropertyName("scheduleName")]
    public string Name { get; set; } = "";

    [Column, NotNull, JsonRequired]
    public bool Status { get; set; }

    [Column, NotNull, JsonRequired]
    public Int32 Value { get; set; }

    public Schedule() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Schedule)));
    }
}