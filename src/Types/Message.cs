using System.Text.Json.Serialization;

namespace Conductor.Types;

public sealed class Message<T>(
    Int32 statusId,
    string info,
    List<T>? values = null,
    bool err = false,
    Int32? page = null
    )
{
    [JsonRequired]
    public Int32 StatusCode { get; set; } = statusId;

    [JsonRequired]
    public string Information { get; set; } = info;

    [JsonRequired]
    public bool Error { get; set; } = err;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Int32? EntityCount { get; set; } = values?.Count;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Int32? Page { get; set; } = page;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<T>? Content { get; set; } = values;
}

public sealed class Message(
    Int32 statusId,
    string info,
    bool err = false
    )
{
    [JsonRequired]
    public Int32 StatusCode { get; set; } = statusId;

    [JsonRequired]
    public string Information { get; set; } = info;

    [JsonRequired]
    public bool Error { get; set; } = err;
}