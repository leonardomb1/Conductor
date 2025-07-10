using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Types;

namespace Conductor.Shared;

public static class Helper
{
    private static readonly Dictionary<Type, long> typeBaseSizes = new()
    {
        [typeof(string)] = Settings.DefaultStringEstimateBytes,
        [typeof(byte[])] = Settings.DefaultByteArrayEstimateBytes,
        [typeof(DateTime)] = sizeof(long),
        [typeof(bool)] = sizeof(bool),
        [typeof(int)] = sizeof(int),
        [typeof(long)] = sizeof(long),
        [typeof(decimal)] = sizeof(decimal),
        [typeof(double)] = sizeof(double),
        [typeof(float)] = sizeof(float),
        [typeof(short)] = sizeof(short),
        [typeof(byte)] = sizeof(byte),
        [typeof(char)] = sizeof(char),
        [typeof(uint)] = sizeof(uint),
        [typeof(ulong)] = sizeof(ulong),
        [typeof(ushort)] = sizeof(ushort),
        [typeof(sbyte)] = sizeof(sbyte)
    };

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetTypeBaseSize(Type type)
    {
        return typeBaseSizes.TryGetValue(type, out var size) ? size : Settings.DefaultTypeEstimateBytes;
    }

    public static long CalculateBytesUsed(DataTable data)
    {
        if (Settings.EnableAccurateMemoryCalculation)
        {
            return CalculateAccurateDataTableBytes(data);
        }
        else
        {
            return EstimateDataTableMemory(data);
        }
    }

    public static long EstimateDataTableMemory(DataTable dataTable)
    {
        if (dataTable?.Rows is null || dataTable.Rows.Count == 0)
            return Settings.DataTableColumnOverheadBytes * (dataTable?.Columns.Count ?? 0);

        long totalBytes = 0;
        var rowCount = dataTable.Rows.Count;
        var columnCount = dataTable.Columns.Count;

        foreach (DataColumn column in dataTable.Columns)
        {
            var baseTypeSize = GetTypeBaseSize(column.DataType);

            if (column.DataType == typeof(string))
            {
                totalBytes += EstimateStringColumnSize(dataTable, column, rowCount);
            }
            else if (column.DataType == typeof(byte[]))
            {
                totalBytes += EstimateByteArrayColumnSize(dataTable, column, rowCount);
            }
            else
            {
                totalBytes += baseTypeSize * rowCount;
            }
        }

        totalBytes += Settings.DataTableColumnOverheadBytes * columnCount;
        totalBytes += Settings.DataTableRowOverheadBytes * rowCount;
        totalBytes += Settings.DataTableStructureOverheadBytes;

        return totalBytes;
    }

    public static long CalculateAccurateDataTableBytes(DataTable dataTable)
    {
        if (dataTable?.Rows is null || dataTable.Rows.Count == 0)
            return Settings.DataTableColumnOverheadBytes * (dataTable?.Columns.Count ?? 0);

        long totalBytes = 0;

        foreach (DataRow row in dataTable.Rows)
        {
            foreach (DataColumn column in dataTable.Columns)
            {
                var value = row[column];
                if (value is null || value == DBNull.Value) continue;

                totalBytes += GetAccurateValueSize(value, column.DataType);
            }
        }

        totalBytes += Settings.DataTableColumnOverheadBytes * dataTable.Columns.Count;
        totalBytes += Settings.DataTableRowOverheadBytes * dataTable.Rows.Count;
        totalBytes += Settings.DataTableStructureOverheadBytes;

        return totalBytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetAccurateValueSize(object value, Type type)
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
            _ when type == typeof(char) => sizeof(char),
            _ when type == typeof(uint) => sizeof(uint),
            _ when type == typeof(ulong) => sizeof(ulong),
            _ when type == typeof(ushort) => sizeof(ushort),
            _ when type == typeof(sbyte) => sizeof(sbyte),
            _ => GetFallbackValueSize(value)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetFallbackValueSize(object value)
    {
        var str = value.ToString();
        return str?.Length * sizeof(char) ?? Settings.DefaultTypeEstimateBytes;
    }

    private static long EstimateStringColumnSize(DataTable dataTable, DataColumn column, int rowCount)
    {
        if (rowCount == 0) return 0;

        var sampleSize = Math.Min(Settings.StringEstimateSampleSize, rowCount);
        var sampleRows = GetSampleRows(dataTable, sampleSize);

        long totalSampleLength = 0;
        int validSamples = 0;

        foreach (var row in sampleRows)
        {
            var value = row[column];
            if (value is not null && value != DBNull.Value && value is string str)
            {
                totalSampleLength += Encoding.UTF8.GetByteCount(str);
                validSamples++;
            }
        }

        if (validSamples == 0)
            return Settings.DefaultStringEstimateBytes * rowCount;

        var averageStringSize = totalSampleLength / validSamples;
        var adjustedAverage = Math.Max(averageStringSize, Settings.MinStringEstimateBytes);

        return adjustedAverage * rowCount;
    }

    private static long EstimateByteArrayColumnSize(DataTable dataTable, DataColumn column, int rowCount)
    {
        if (rowCount == 0) return 0;

        var sampleSize = Math.Min(Settings.ByteArrayEstimateSampleSize, rowCount);
        var sampleRows = GetSampleRows(dataTable, sampleSize);

        long totalSampleLength = 0;
        int validSamples = 0;

        foreach (var row in sampleRows)
        {
            var value = row[column];
            if (value is not null && value != DBNull.Value && value is byte[] bytes)
            {
                totalSampleLength += bytes.LongLength;
                validSamples++;
            }
        }

        if (validSamples == 0)
            return Settings.DefaultByteArrayEstimateBytes * rowCount;

        var averageArraySize = totalSampleLength / validSamples;
        return averageArraySize * rowCount;
    }

    private static IEnumerable<DataRow> GetSampleRows(DataTable dataTable, int sampleSize)
    {
        var rowCount = dataTable.Rows.Count;
        if (sampleSize >= rowCount)
        {
            return dataTable.Rows.Cast<DataRow>();
        }

        var step = Math.Max(1, rowCount / sampleSize);
        var samples = new List<DataRow>(sampleSize);

        for (int i = 0; i < rowCount && samples.Count < sampleSize; i += step)
        {
            samples.Add(dataTable.Rows[i]);
        }

        return samples;
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
        return GetAccurateValueSize(value, type);
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