
using System.Data.Common;
using Conductor.Model;
using Conductor.Service.Database;

namespace Conductor.Service.Script;


public class ScriptContext
{
    public required Extraction Extraction { get; set; }
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    public int? OverrideFilter { get; set; }
    public DBExchange? DbExchange { get; set; }
    public DbConnection? DbConnection { get; set; } // Added for direct DB access
    public HttpClient? HttpClient { get; set; }
    public ILogger? Logger { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = [];
    public string? UserId { get; set; }
    public string? TenantId { get; set; }
}