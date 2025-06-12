using Conductor.Model;
using Conductor.Service.Database;

namespace Conductor.Service.Script;


public class ScriptContext
{
    public Extraction Extraction { get; set; } = null!;
    public DateTime RequestTime { get; set; }
    public int? OverrideFilter { get; set; }
    public DBExchange? DbExchange { get; set; }
    public HttpClient? HttpClient { get; set; }
    public ILogger? Logger { get; set; }
}