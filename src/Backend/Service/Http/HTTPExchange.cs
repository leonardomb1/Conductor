using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;

namespace Conductor.Service.Http;

public abstract class HTTPExchange(IHttpClientFactory factory)
{
    public virtual async ValueTask<Result<JsonElement>> FetchEndpointData(
        Extraction extraction,
        Func<HttpClient, string, JsonObject?, Task<Result<HttpResponseMessage>>> httpMethod
    )
    {
        if (extraction.EndpointFullName is null)
        {
            return new Error("No endpoint was passed.");
        }

        var client = factory.CreateClient();

        client.DefaultRequestHeaders.UserAgent.Add(
            ProductInfoHeaderValue.Parse($"{ProgramInfo.ProgramName}/{ProgramInfo.ProgramVersion}")
        );

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );

        ApplyHeaders(client, extraction);

        JsonObject? data = FormBody(extraction);

        var res = await httpMethod(client, extraction.EndpointFullName, data);
        if (!res.IsSuccessful) return res.Error;

        string json = await res.Value.Content.ReadAsStringAsync();

        return JsonDocument.Parse(json).RootElement;
    }

    protected async Task<Result<HttpResponseMessage>> Get(HttpClient client, string uri)
    {
        try
        {
            var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            return response;
        }
        catch (Exception ex)
        {
            return new Error($"Failed to make request: {ex.Message}");
        }
    }

    protected async Task<Result<HttpResponseMessage>> Post(HttpClient client, string uri, JsonObject data)
    {
        try
        {
            var response = await client.PostAsJsonAsync(uri, data);
            response.EnsureSuccessStatusCode();

            return response;
        }
        catch (Exception ex)
        {
            return new Error($"Failed to make request: {ex.Message}");
        }
    }

    protected void ApplyHeaders(HttpClient client, Extraction extraction)
    {
        if (extraction.HeaderStructure is null) return;

        string[] sets = extraction.HeaderStructure.Split(Settings.SplitterChar);
        foreach (string set in sets)
        {
            string[] keyValue = set.Split("=");
            if (keyValue.Length != 2) return;

            string key = keyValue[0];
            string value = keyValue[1];

            if (
                value.StartsWith(Settings.EncryptIndicatorBegin) &&
                value.EndsWith(Settings.EncryptIndicatorEnd)
            )
            {
                string toDecrypt = value.Substring(
                    Settings.EncryptIndicatorBegin.Length,
                    value.Length - Settings.EncryptIndicatorEnd.Length
                );

                value = Encryption.SymmetricDecryptAES256(toDecrypt, Settings.EncryptionKey);
            }

            client.DefaultRequestHeaders.Add(key, value);
        }
    }

    protected virtual JsonObject? FormBody(Extraction extraction)
    {
        if (extraction.BodyStructure is null) return null;

        JsonObject json = [];

        string[] sets = extraction.BodyStructure.Split(Settings.SplitterChar);
        foreach (string set in sets)
        {
            string[] keyValue = set.Split("=");
            if (keyValue.Length != 2) return null;

            string key = keyValue[0];
            string value = keyValue[1];

            if (
                value.StartsWith(Settings.EncryptIndicatorBegin) &&
                value.EndsWith(Settings.EncryptIndicatorEnd)
            )
            {
                string toDecrypt = value[Settings.EncryptIndicatorBegin.Length..^Settings.EncryptIndicatorEnd.Length];
                value = Encryption.SymmetricDecryptAES256(toDecrypt, Settings.EncryptionKey);
            }

            json.Add(key, JsonNode.Parse(value)
            );
        }

        return json;
    }
}