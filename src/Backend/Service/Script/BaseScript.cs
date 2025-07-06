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
}