using System.Text.Json.Nodes;
using Conductor.Types;

namespace Conductor.Service.Http;

public class GenericRequest(IHttpClientFactory factory) : HTTPExchange(factory)
{
    public new async Task<Result<HttpResponseMessage>> Get(HttpClient client, string uri)
        => await base.Get(client, uri);

    public new async Task<Result<HttpResponseMessage>> Post(HttpClient client, string uri, JsonObject data)
        => await base.Post(client, uri, data);
}
