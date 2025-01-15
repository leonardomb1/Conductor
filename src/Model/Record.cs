using EFTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using LdbTable = LinqToDB.Mapping.TableAttribute;
using System.Text.Json.Serialization;
using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace Conductor.Model;

[LdbTable(tableName), EFTable(tableName)]
public sealed class Record : IDbModel, IEndpointParameterMetadataProvider
{
    private const string tableName = "RECORDS";

    [PrimaryKey, Identity]
    [Key]
    public UInt32 Id { get; set; }

    [Column, JsonRequired, NotNull]
    public string HostName { get; set; } = "";

    [Column, JsonRequired, NotNull]
    public DateTime TimeStamp { get; set; }

    [Column, JsonRequired, NotNull]
    public string EventType { get; set; } = "";

    [Column, JsonRequired, NotNull]
    public string CallerMethod { get; set; } = "";

    [Column, JsonRequired, NotNull]
    public string Event { get; set; } = "";

    public Record() { }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Record)));
    }
}