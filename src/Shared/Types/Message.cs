using System.Net;
using System.Text.Json.Serialization;

namespace Conductor.Shared.Types;

public sealed class Message<T>
{
    [JsonRequired]
    public Int32 StatusCode { get; set; }

    [JsonRequired]
    public string Information { get; set; }

    [JsonRequired]
    public bool Error { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Int32? EntityCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<T>? Content { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Int32? Page { get; set; }

    public Message(
        Int32 statusId,
        string info,
        List<T>? values = null,
        bool err = false,
        Int32? page = null
    )
    {
        StatusCode = statusId;
        Information = info;
        Error = err;
        Content = values;
        EntityCount = values?.Count;
        Page = page;
    }
}

public sealed class Message
{
    [JsonRequired]
    public Int32 StatusCode { get; set; }

    [JsonRequired]
    public string Information { get; set; }

    [JsonRequired]
    public bool Error { get; set; }

    public Message(
        Int32 statusId,
        string info,
        bool err = false
    )
    {
        StatusCode = statusId;
        Information = info;
        Error = err;
    }
}