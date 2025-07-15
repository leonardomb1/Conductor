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
    public bool? HasNestedData { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FetchMetadata? Metadata { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<T>? Content { get; set; } = values;

    public static FetchResponse<T> FetchSuccess(List<T> data, int? page = null, int? pageSize = null, bool hasNestedData = false, FetchMetadata? metadata = null)
    {
        return new FetchResponse<T>
        {
            StatusCode = 200,
            Information = "OK",
            Error = false,
            EntityCount = data.Count,
            Page = page,
            HasNestedData = hasNestedData,
            Metadata = metadata,
            Content = data
        };
    }

    public static FetchResponse<T> FetchNotFound(string message = "Requested resource was not found.")
    {
        return new FetchResponse<T>
        {
            StatusCode = 200,
            Information = message,
            Error = false,
            Content = null
        };
    }
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

public sealed class FetchMetadata
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtractionName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? ExtractionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? RequestTime { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? ProcessingTime { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DataSizeBytes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? NestedProperties { get; init; }
}