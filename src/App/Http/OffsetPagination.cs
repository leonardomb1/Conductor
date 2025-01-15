using System.Text.Json.Nodes;
using Conductor.Model;

namespace Conductor.App.Http;

public class OffsetPagination(IServiceProvider service) : HTTPExchange(service)
{
    protected override JsonObject? FormBody(Extraction extraction)
    {
        var body = base.FormBody(extraction) ?? [];

        if (extraction.OffsetLimitAttr != null && extraction.OffsetAttr != null)
        {
            body[extraction.OffsetAttr] = 0;
            body[extraction.OffsetLimitAttr] = 100;
        }

        return body;
    }
}