using System.Text.Json.Nodes;
using Conductor.Model;

namespace Conductor.Service.Http;

public class OffsetPagination(IHttpClientFactory factory) : HTTPExchange(factory)
{
    protected override JsonObject? FormBody(Extraction extraction)
    {
        var body = base.FormBody(extraction) ?? [];

        if (extraction.OffsetLimitAttr is not null && extraction.OffsetAttr is not null)
        {
            body[extraction.OffsetAttr] = 0;
            body[extraction.OffsetLimitAttr] = 100;
        }

        return body;
    }
}
