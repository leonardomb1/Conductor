using System.Data;
using System.Text;
using System.Text.Json;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Types;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace Conductor.Shared;

public static class Helper
{
    public static void ShowHelp()
    {
        ShowSignature();
        Console.WriteLine(
            $"Usage: {ProgramInfo.VersionHeader} \n" +
            "   [Options]: \n" +
            "   -h --help      Show this help message\n" +
            "   -v --version   Show version information\n" +
            "   -e --environment  Use environment variables for configuration\n" +
            "   -f --file  Use .env file for configuration\n" +
            "   -M --migrate Runs a migration in the configured .env database\n" +
            "   -eM --migrate-init-env  Runs a migration before running the server, uses the environment variables for configuration\n" +
            "   -fM --migrate-init-file  Runs a migration before running the server, uses the .env file for configuration\n"
            );
    }

    public static void ShowVersion()
    {
        Console.WriteLine(ProgramInfo.VersionHeader);
    }

    private static void ShowSignature()
    {
        Console.WriteLine(
            "Developed by Leonardo M. Baptista\n"
        );
    }

    public static long CalculateBytesUsed(DataTable data)
    {
        long bytes = 0;
        foreach (DataRow row in data.Rows)
        {
            foreach (DataColumn col in data.Columns)
            {
                if (row[col] is null || row[col] == DBNull.Value) continue;
                bytes += GetTypeByteSize(row[col], col.DataType);
            }
        }
        return bytes;
    }

    public static async Task SendErrorNotification(IHttpClientFactory clientFactory, Error[] errors)
    {
        using HttpClient client = clientFactory.CreateClient();

        try
        {
            var card = new
            {
                @type = "MessageCard",
                @context = "http://schema.org/extensions",
                summary = "Pipeline Errors",
                themeColor = "FF0000",
                title = "Pipeline Event Run",
                text = "An event has been run and pipeline errors were detected.",
                sections = new[]
                {
                    new {
                        facts = errors.Select(e => new {
                            name = e.ExceptionMessage ?? "Error",
                            value = e.StackTrace ?? "No details"
                        }).ToArray()
                    }
                }
            };

            string json = JsonSerializer.Serialize(card);

            using var request = new HttpRequestMessage(HttpMethod.Post, Settings.WebhookUri)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await client.SendAsync(request);
        }
        finally
        {
            client.Dispose();
        }
    }

    public static void GetAndSetByteUsageForExtraction(DataTable data, uint id, IJobTracker jobTracker)
    {
        long byteSize = CalculateBytesUsed(data);
        var job = jobTracker.GetJobByExtractionId(id);
        if (job is not null)
        {
            jobTracker.UpdateTransferedBytes(id, byteSize);
        }
    }

    public static long GetTypeByteSize(object value, Type type)
    {
        return type switch
        {
            _ when type == typeof(string) => Encoding.UTF8.GetByteCount((string)value),
            _ when type == typeof(byte[]) => ((byte[])value).LongLength,
            _ when type == typeof(DateTime) => sizeof(long),
            _ when type == typeof(bool) => sizeof(bool),
            _ when type == typeof(int) => sizeof(int),
            _ when type == typeof(long) => sizeof(long),
            _ when type == typeof(decimal) => sizeof(decimal),
            _ when type == typeof(double) => sizeof(double),
            _ when type == typeof(float) => sizeof(float),
            _ when type == typeof(short) => sizeof(short),
            _ when type == typeof(byte) => sizeof(byte),
            _ => value.ToString()?.Length * sizeof(char) ?? 0
        };
    }

    public static void DecryptConnectionStrings(List<Extraction> extractions, string? encryptionKey = null)
    {
        if (extractions is null || extractions.Count == 0)
            return;

        var keyToUse = encryptionKey ?? Settings.EncryptionKey;

        if (string.IsNullOrEmpty(keyToUse))
            throw new InvalidOperationException("Encryption key is required for decrypting connection strings");

        var decryptedOriginIds = new HashSet<uint>();
        var decryptedDestinationIds = new HashSet<uint>();

        foreach (var extraction in extractions)
        {
            if (!string.Equals(extraction.SourceType, "db", StringComparison.OrdinalIgnoreCase))
                continue;

            if (extraction.OriginId.HasValue &&
                extraction.Origin is not null &&
                extraction.Origin.ConnectionString is not null &&
                !decryptedOriginIds.Contains(extraction.OriginId.Value))
            {
                extraction.Origin.ConnectionString = Encryption.SymmetricDecryptAES256(
                    extraction.Origin.ConnectionString,
                    keyToUse
                );
                decryptedOriginIds.Add(extraction.OriginId.Value);
            }

            if (extraction.DestinationId.HasValue &&
                extraction.Destination is not null &&
                extraction.Destination.ConnectionString is not null &&
                !decryptedDestinationIds.Contains(extraction.DestinationId.Value))
            {
                extraction.Destination.ConnectionString = Encryption.SymmetricDecryptAES256(
                    extraction.Destination.ConnectionString,
                    keyToUse
                );
                decryptedDestinationIds.Add(extraction.DestinationId.Value);
            }
        }
    }
}