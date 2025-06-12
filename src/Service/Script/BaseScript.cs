using System.Data;

namespace Conductor.Service.Script;

public abstract class BaseScript : IScript
{
    public abstract Task<DataTable> ExecuteAsync<T>(T context, CancellationToken cancellationToken = default) where T : class;

    protected DataTable CreateDataTable(params (string name, Type type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
        {
            table.Columns.Add(name, type);
        }
        return table;
    }

    protected DataTable CreateDataTable(IEnumerable<string> columnNames)
    {
        var table = new DataTable();
        foreach (var name in columnNames)
        {
            table.Columns.Add(name, typeof(string));
        }
        return table;
    }

    protected DataRow AddRow(DataTable table, params object[] values)
    {
        var row = table.NewRow();
        for (int i = 0; i < values.Length && i < table.Columns.Count; i++)
        {
            row[i] = values[i] ?? DBNull.Value;
        }
        table.Rows.Add(row);
        return row;
    }

    protected DataRow AddRow(DataTable table, Dictionary<string, object> values)
    {
        var row = table.NewRow();
        foreach (var kvp in values)
        {
            if (table.Columns.Contains(kvp.Key))
            {
                row[kvp.Key] = kvp.Value ?? DBNull.Value;
            }
        }
        table.Rows.Add(row);
        return row;
    }

    protected void LogInfo(ScriptContext context, string message)
    {
        context.Logger?.LogInformation($"[Script] {message}");
    }

    protected void LogError(ScriptContext context, string message, Exception? ex = null)
    {
        context.Logger?.LogError(ex, $"[Script] {message}");
    }

    protected async Task<string> HttpGetAsync(ScriptContext context, string url, CancellationToken cancellationToken = default)
    {
        if (context.HttpClient == null)
            throw new InvalidOperationException("HttpClient not available in script context");

        var response = await context.HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}