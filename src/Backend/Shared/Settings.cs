using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using NetTools;
using Serilog;
using Conductor.Types;

namespace Conductor.Shared;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ConfigKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}

public static class Settings
{
    public static Lazy<ParallelOptions> ParallelRule => new(() => new() { MaxDegreeOfParallelism = MaxDegreeParallel });

    public static Lazy<IPAddressRange[]> AllowedIpsRange => new(() =>
    {
        if (AllowedIps.IsNullOrEmpty()) return [];

        string[] ranges = AllowedIps.Split(SplitterChar);
        IPAddressRange[] arrayOfRanges = new IPAddressRange[ranges.Length];

        for (byte i = 0; i < ranges.Length; i++)
        {
            if (!IPAddressRange.TryParse(ranges[i], out var pRange))
            {
                Log.Error($"Invalid IP address range configured: {ranges[i]}");
                continue;
            }
            arrayOfRanges[i] = pRange;
        }

        return arrayOfRanges;
    });

    public static Lazy<HashSet<string>> AllowedCorsSet => new(() => AllowedCors?.Split(SplitterChar).ToHashSet() ?? []);

    public static Lazy<JsonSerializerOptions> JsonSOptions => new(() => new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    });

    public static bool IsMasterNode => NodeType == Node.MasterCluster || NodeType == Node.MasterPrincipal;

    [ConfigKey("NODE_TYPE")]
    public static Node NodeType { get; set; } = Node.Single;

    [ConfigKey("MASTER_NODE_ENDPOINT")]
    public static string MasterNodeEndpoint { get; set; } = "";

    [ConfigKey("FILE_STREAM_BUFFER_SIZE")]
    public static int FileStreamBufferSize { get; set; }

    [ConfigKey("CONNECTION_POOL_MAX_SIZE")]
    public static int ConnectionPoolMaxSize { get; set; }

    [ConfigKey("MASTER_NODE_CPU_REDIRECT_PERCENTAGE")]
    public static double MasterNodeCpuRedirectPercentage { get; set; }

    [ConfigKey("CONNECTION_POOL_MIN_SIZE")]
    public static int ConnectionPoolMinSize { get; set; }

    [ConfigKey("CONNECTION_IDLE_TIMEOUT_MINUTES")]
    public static int ConnectionIdleTimeoutMinutes { get; set; }

    [ConfigKey("DATATABLE_LIFETIME_MINUTES")]
    public static int DataTableLifetimeMinutes { get; set; }

    [ConfigKey("CHANNEL_MAXIMUM_SIZE")]
    public static int ChannelMaximumSize { get; set; }

    [ConfigKey("DATATABLE_CLEANUP_INTERVAL_MINUTES")]
    public static int DataTableCleanupIntervalMinutes { get; set; }

    [ConfigKey("GC_CHECK_INTERVAL_MINUTES")]
    public static int GcCheckIntervalMinutes { get; set; }

    [ConfigKey("MEMORY_PRESSURE_THRESHOLD_GB")]
    public static double MemoryPressureThresholdGB { get; set; }

    [ConfigKey("MEMORY_PRESSURE_FORCE_GC_MULTIPLIER")]
    public static double MemoryPressureForceGcMultiplier { get; set; }

    [ConfigKey("DATATABLE_DISPOSE_TIMEOUT_SECONDS")]
    public static int DataTableDisposeTimeoutSeconds { get; set; }

    [ConfigKey("DATATABLE_COLUMN_OVERHEAD_BYTES")]
    public static long DataTableColumnOverheadBytes { get; set; }

    [ConfigKey("DATATABLE_ROW_OVERHEAD_BYTES")]
    public static long DataTableRowOverheadBytes { get; set; }

    [ConfigKey("DATATABLE_STRUCTURE_OVERHEAD_BYTES")]
    public static long DataTableStructureOverheadBytes { get; set; }

    [ConfigKey("DEFAULT_STRING_ESTIMATE_BYTES")]
    public static long DefaultStringEstimateBytes { get; set; }

    [ConfigKey("DEFAULT_BYTE_ARRAY_ESTIMATE_BYTES")]
    public static long DefaultByteArrayEstimateBytes { get; set; }

    [ConfigKey("DEFAULT_TYPE_ESTIMATE_BYTES")]
    public static long DefaultTypeEstimateBytes { get; set; }

    [ConfigKey("STRING_ESTIMATE_SAMPLE_SIZE")]
    public static int StringEstimateSampleSize { get; set; }

    [ConfigKey("BYTE_ARRAY_ESTIMATE_SAMPLE_SIZE")]
    public static int ByteArrayEstimateSampleSize { get; set; }

    [ConfigKey("MIN_STRING_ESTIMATE_BYTES")]
    public static long MinStringEstimateBytes { get; set; }

    [ConfigKey("ENABLE_ACCURATE_MEMORY_CALCULATION")]
    public static bool EnableAccurateMemoryCalculation { get; set; }

    [ConfigKey("LOGGING_LEVEL_DEBUG")]
    public static bool LogLevelDebug { get; set; }

    [ConfigKey("SEND_WEBHOOK_ON_ERROR")]
    public static bool SendWebhookOnError { get; set; }

    [ConfigKey("WEBHOOK_URI")]
    public static string WebhookUri { get; set; } = "";

    [ConfigKey("ADMIN_LOGIN")]
    public static string AdminLogin { get; set; } = "";

    [ConfigKey("ADMIN_PASSWORD")]
    public static string AdminPassword { get; set; } = "";

    [ConfigKey("MAX_DEGREE_PARALLEL")]
    public static int MaxDegreeParallel { get; set; }

    [ConfigKey("CSV_OUTPUT_PATH")]
    public static string CsvOutputPath { get; set; } = "";

    [ConfigKey("MAX_CONCURRENT_CONNECTIONS")]
    public static int MaxConcurrentConnections { get; set; }

    [ConfigKey("MAX_CONSUMER_FETCH")]
    public static int ConsumerFetchMax { get; set; }

    [ConfigKey("MAX_PRODUCER_LINECOUNT")]
    public static ulong ProducerLineMax { get; set; }

    [ConfigKey("MAX_FETCHING_LINECOUNT")]
    public static ulong FetcherLineMax { get; set; }

    [ConfigKey("SESSION_TIME_SEC")]
    public static uint SessionTime { get; set; }

    [ConfigKey("QUERY_TIMEOUT_SEC")]
    public static int QueryTimeout { get; set; }

    [ConfigKey("MAX_PIPELINE_ATTEMPT")]
    public static byte PipelineAttemptMax { get; set; }

    [ConfigKey("PORT_NUMBER")]
    public static int PortNumber { get; set; }

    [ConfigKey("LDAP_PORT")]
    public static int LdapPort { get; set; }

    [ConfigKey("CONNECTION_STRING")]
    public static string ConnectionString { get; set; } = "";

    [ConfigKey("LDAP_SSL")]
    public static bool LdapSsl { get; set; }

    [ConfigKey("USE_HTTPS")]
    public static bool UseHttps { get; set; }

    [ConfigKey("LDAP_VERIFY_CERTIFICATE")]
    public static bool LdapVerifyCertificate { get; set; }

    [ConfigKey("DB_TYPE")]
    public static string DbType { get; set; } = "";

    [ConfigKey("ENCRYPT_KEY")]
    public static string EncryptionKey { get; set; } = "";

    [ConfigKey("LDAP_SERVER")]
    public static string LdapServer { get; set; } = "";

    [ConfigKey("API_KEY")]
    public static string ApiKey { get; set; } = "";

    [ConfigKey("LDAP_DOMAIN")]
    public static string LdapDomain { get; set; } = "";

    [ConfigKey("LDAP_BASEDN")]
    public static string LdapBaseDn { get; set; } = "";

    [ConfigKey("LDAP_GROUPS")]
    public static string LdapGroups { get; set; } = "";

    [ConfigKey("NODES")]
    public static string Nodes { get; set; } = "";

    [ConfigKey("ALLOWED_IP_ADDRESSES")]
    public static string AllowedIps { get; set; } = "";

    [ConfigKey("ALLOWED_CORS")]
    public static string AllowedCors { get; set; } = "";

    [ConfigKey("LDAP_GROUPDN")]
    public static string LdapGroupDN { get; set; } = "";

    [ConfigKey("REQUIRE_AUTHENTICATION")]
    public static bool RequireAuthentication { get; set; }

    [ConfigKey("CONNECTION_TIMEOUT_MIN")]
    public static int ConnectionTimeout { get; set; }

    [ConfigKey("RESPONSE_CACHING_LIMIT_MB")]
    public static int ResponseCachingLimit { get; set; }

    [ConfigKey("PIPELINE_BACKOFF_BASE_MS")]
    public static int PipelineBackoff { get; set; }

    [ConfigKey("SPLITTER_CHAR")]
    public static string SplitterChar { get; set; } = "";

    [ConfigKey("CERTIFICATE_PASSWORD")]
    public static string CertificatePassword { get; set; } = "";

    [ConfigKey("CERTIFICATE_PATH")]
    public static string CertificatePath { get; set; } = "";

    [ConfigKey("ENCRYPT_INDICATOR_BEGIN")]
    public static string EncryptIndicatorBegin { get; set; } = "";

    [ConfigKey("ENCRYPT_INDICATOR_END")]
    public static string EncryptIndicatorEnd { get; set; } = "";

    [ConfigKey("DEVELOPMENT_MODE")]
    public static bool DevelopmentMode { get; set; }

    [ConfigKey("VERIFY_TCP")]
    public static bool VerifyTCP { get; set; }

    [ConfigKey("VERIFY_CORS")]
    public static bool VerifyCors { get; set; }
}
