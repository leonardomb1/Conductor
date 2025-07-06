using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Conductor.Types;

public sealed class Error(
    string msg,
    string? stk = null,
    bool partialSuccess = false,
    [CallerMemberName] string? method = null
    )
{
    [JsonRequired]
    public string ExceptionMessage { get; set; } = msg;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FaultedMethod { get; set; } = method ?? "n/a";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPartialSuccess { get; set; } = partialSuccess;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StackTrace { get; set; } = stk;
}