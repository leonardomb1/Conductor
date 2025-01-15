using System.Text.Json.Nodes;
using Conductor.Model;

namespace Conductor.App.Http;

public class PageNumberPagination(IServiceProvider service) : HTTPExchange(service)
{
    protected override JsonObject? FormBody(Extraction extraction)
    {
        var body = base.FormBody(extraction) ?? [];

        if (extraction.TotalPageAttr != null && extraction.PageAttr != null)
        {
            body[extraction.PageAttr] = 0;
            body[extraction.TotalPageAttr] = 100;
        }

        return body;
    }
}