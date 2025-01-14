using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Conductor.Shared.Types;

public sealed class Error
{
    [JsonRequired]
    public string ExceptionMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FaultedMethod { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPartialSuccess { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StackTrace { get; set; }

    public Error(
        string msg,
        string? stk = null,
        bool partialSuccess = false,
        [CallerMemberName] string? method = null
    )
    {
        ExceptionMessage = msg;
        StackTrace = stk;
        IsPartialSuccess = partialSuccess;
        FaultedMethod = method ?? "n/a";
    }
}