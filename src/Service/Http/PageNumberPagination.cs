using System.Text.Json.Nodes;
using Conductor.Model;

namespace Conductor.Service.Http;

public class PageNumberPagination(IHttpClientFactory factory) : HTTPExchange(factory)
{
    protected override JsonObject? FormBody(Extraction extraction)
    {
        var body = base.FormBody(extraction) ?? [];

        if (extraction.TotalPageAttr is not null && extraction.PageAttr is not null)
        {
            body[extraction.PageAttr] = 0;
            body[extraction.TotalPageAttr] = 100;
        }

        return body;
    }
}
