using System.Text.Json.Serialization;

namespace Conductor.Types;

public sealed class Message<T>(
    int statusId,
    string info,
    List<T>? values = null,
    bool err = false,
    int? page = null
    )
{
    [JsonRequired]
    public int StatusCode { get; set; } = statusId;

    [JsonRequired]
    public string Information { get; set; } = info;

    [JsonRequired]
    public bool Error { get; set; } = err;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EntityCount { get; set; } = values?.Count;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Page { get; set; } = page;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<T>? Content { get; set; } = values;
}

public sealed class Message(
    int statusId,
    string info,
    bool err = false
    )
{
    [JsonRequired]
    public int StatusCode { get; set; } = statusId;

    [JsonRequired]
    public string Information { get; set; } = info;

    [JsonRequired]
    public bool Error { get; set; } = err;
}