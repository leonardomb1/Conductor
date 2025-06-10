using System.Text.Json.Nodes;
using Conductor.Types;

namespace Conductor.Service.Http;

public static class HTTPExchangeFactory
{
    public static (HTTPExchange exchange, Func<HttpClient, string, JsonObject?, Task<Result<HttpResponseMessage>>> httpMethodCallback) Create(
        IHttpClientFactory factory,
        string? paginationType = null,
        string? httpMethod = null
    )
    {
        HTTPExchange exchange = paginationType?.ToLower() switch
        {
            "pagenumber" or "page" or "page_number" => new PageNumberPagination(factory),
            "offset" or "offsetpagination" => new OffsetPagination(factory),
            _ => new GenericRequest(factory)
        };

        Func<HttpClient, string, JsonObject?, Task<Result<HttpResponseMessage>>> methodCallback =
            (httpMethod?.ToUpperInvariant() ?? "GET") switch
            {
                "POST" => async (client, uri, data) => await ((GenericRequest)exchange).Post(client, uri, data!),
                _ => async (client, uri, data) => await ((GenericRequest)exchange).Get(client, uri)
            };

        return (exchange, methodCallback);
    }
}
